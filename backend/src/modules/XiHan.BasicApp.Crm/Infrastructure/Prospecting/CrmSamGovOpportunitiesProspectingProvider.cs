using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// SAM.gov public opportunities search. API keys are resolved from the current tenant's encrypted SaaS config.
/// Procurement notices are evidence, not qualified CRM leads.
/// </summary>
public sealed class CrmSamGovOpportunitiesProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.sam.gov/opportunities/v2/search");
    private const int PageSize = 100;
    private const int MaxPages = 3;
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly Func<CancellationToken, Task<string?>> _apiKeyFactory;

    public CrmSamGovOpportunitiesProspectingProvider(HttpClient httpClient, string? apiKey)
        : this(httpClient, _ => Task.FromResult(string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim())) { }

    public CrmSamGovOpportunitiesProspectingProvider(HttpClient httpClient, ISaasConfigurationService configuration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(configuration);
        _apiKeyFactory = cancellationToken => configuration.GetStringAsync(
            "CRM.Prospecting.Provider.sam_gov.ApiKey", cancellationToken: cancellationToken);
    }

    private CrmSamGovOpportunitiesProspectingProvider(HttpClient httpClient, Func<CancellationToken, Task<string?>> keyFactory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKeyFactory = keyFactory;
    }

    public string ProviderCode => "sam_gov";
    public void Dispose() => _httpClient.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsUnitedStatesTarget(request.TargetMarkets))
            return new CrmProspectingProviderResult([], 0, ["SAM.gov was skipped because the campaign target market does not include the United States."]) { IsComplete = true };

        var apiKey = await _apiKeyFactory(cancellationToken);
        apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        if (apiKey is null)
            return new CrmProspectingProviderResult([], 1, ["SAM.gov Opportunities API requires a tenant API key. Configure it in the prospecting provider credentials panel."]) { IsComplete = false };

        var keywords = Regex.Split($"{request.ProductScope} {request.SearchObjective} {request.IdealCustomerProfile}", @"[^\p{L}\p{N}]+")
            .Where(value => value.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
        if (keywords.Length == 0)
            return new CrmProspectingProviderResult([], 0, ["SAM.gov search requires product or industry keywords."]) { IsComplete = false };

        var postedTo = DateTime.UtcNow.Date;
        var postedFrom = postedTo.AddDays(-30);
        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        var retrievedAt = DateTimeOffset.UtcNow;
        long totalRecords = 0;
        var processed = 0;
        var lastPageCount = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = new Dictionary<string, string>
            {
                ["api_key"] = apiKey,
                ["postedFrom"] = postedFrom.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
                ["postedTo"] = postedTo.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
                ["limit"] = PageSize.ToString(CultureInfo.InvariantCulture),
                ["offset"] = page.ToString(CultureInfo.InvariantCulture),
                ["title"] = string.Join(' ', keywords.Take(3))
            };
            var builder = new UriBuilder(Endpoint)
            {
                Query = string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
            };
            using var message = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
            message.Headers.Accept.ParseAdd("application/json");
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
                throw new HttpRequestException("SAM.gov response came from an unapproved endpoint.");
            if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
            {
                var retryAt = response.Headers.RetryAfter?.Date
                    ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddMinutes(5));
                throw new CrmProspectingProviderRateLimitException("SAM.gov throttled the request; retry after the supplied cooldown.", (int)response.StatusCode, retryAt);
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("SAM.gov Opportunities API returned an HTTP error.", null, response.StatusCode);

            using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty("opportunitiesData", out var opportunities) || opportunities.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("SAM.gov response did not contain a valid opportunitiesData array.");
            totalRecords = ReadInt64(root, "totalRecords");
            lastPageCount = opportunities.GetArrayLength();
            processed += lastPageCount;

            foreach (var notice in opportunities.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = ReadString(notice, "noticeId");
                var title = ReadString(notice, "title");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                {
                    warnings.Add("A SAM.gov notice without a stable notice ID or title was skipped.");
                    continue;
                }
                var buyer = FirstNonEmpty(
                    ReadString(notice, "fullParentPathName"),
                    ReadString(notice, "department"),
                    ReadString(notice, "subTier"),
                    "U.S. federal contracting agency");
                var solicitation = ReadString(notice, "solicitationNumber");
                var posted = ReadString(notice, "postedDate");
                var deadline = FirstNonEmpty(ReadString(notice, "responseDeadLine"), ReadString(notice, "reponseDeadLine"), ReadString(notice, "responseDeadline"));
                var contact = ReadFirstContact(notice);
                var contactName = ReadString(contact, "fullName");
                var contactEmail = NormalizeEmail(ReadString(contact, "email"));
                var contactPhone = ReadString(contact, "phone");
                var noticeType = ReadString(notice, "type");
                var naics = ReadString(notice, "naicsCode");
                var summary = string.Join("; ", new[]
                {
                    string.IsNullOrWhiteSpace(solicitation) ? null : $"Solicitation {solicitation}",
                    string.IsNullOrWhiteSpace(noticeType) ? null : $"Notice type {noticeType}",
                    string.IsNullOrWhiteSpace(posted) ? null : $"Posted {posted}",
                    string.IsNullOrWhiteSpace(deadline) ? null : $"Response deadline {deadline}",
                    string.IsNullOrWhiteSpace(naics) ? null : $"NAICS {naics}",
                    string.IsNullOrWhiteSpace(contactPhone) ? null : $"Public buyer contact phone {contactPhone}"
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                records.Add(new CrmProspectingRawRecord(
                    $"SAMGOV:{id}",
                    "SAM.gov Opportunities",
                    Limit(title, 500),
                    Limit(buyer, 200),
                    Limit(contactName, 200),
                    contactEmail,
                    Limit(summary, 2000))
                {
                    SourceUrl = $"https://sam.gov/opp/{Uri.EscapeDataString(id)}/view",
                    PublishedAtUtc = ParseDate(posted),
                    RetrievedAtUtc = retrievedAt,
                    RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(notice.GetRawText()))).ToLowerInvariant(),
                    EvidenceType = "official_procurement"
                });
            }

            if ((totalRecords > 0 && processed >= totalRecords) || lastPageCount < PageSize)
                break;
        }

        var complete = totalRecords > 0 ? processed >= totalRecords : lastPageCount < PageSize;
        if (!complete)
            warnings.Add($"SAM.gov pagination stopped after {MaxPages} pages ({processed} records of {totalRecords}); coverage is incomplete.");
        if (records.Count == 0)
            warnings.Add("SAM.gov returned no matching procurement notices for the selected 30-day window.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static bool IsUnitedStatesTarget(string markets)
        => Regex.Split(markets ?? string.Empty, @"[,;，；|]+")
            .Select(value => value.Trim())
            .Any(value => value.Equals("United States", StringComparison.OrdinalIgnoreCase)
                || value.Equals("USA", StringComparison.OrdinalIgnoreCase)
                || value.Equals("US", StringComparison.OrdinalIgnoreCase)
                || value.Equals("United States of America", StringComparison.OrdinalIgnoreCase));

    private static JsonElement ReadFirstContact(JsonElement notice)
    {
        if (!notice.TryGetProperty("pointOfContact", out var contacts) || contacts.ValueKind != JsonValueKind.Array)
            return default;
        foreach (var contact in contacts.EnumerateArray())
            if (contact.ValueKind == JsonValueKind.Object) return contact;
        return default;
    }

    private static string? NormalizeEmail(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Contains('@') ? value.Trim() : null;

    private static long ReadInt64(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) ? number : 0;

    private static string? ReadString(JsonElement element, string property)
        => !element.TryGetProperty(property, out var value) ? null
            : value.ValueKind == JsonValueKind.String ? value.GetString()
            : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToUniversalTime() : null;

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string Limit(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= max ? value : value[..max];

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
            throw new InvalidDataException("SAM.gov response exceeded the 8 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes) throw new InvalidDataException("SAM.gov response exceeded the 8 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
