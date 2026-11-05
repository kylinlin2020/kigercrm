using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// World Bank public procurement notice search. Records are procurement evidence, not qualified CRM leads.
/// The current provider contract has no continuation cursor, so this adapter deliberately reads one bounded page
/// and marks coverage incomplete whenever the upstream response indicates additional notices.
/// </summary>
public sealed class CrmWorldBankProcurementProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://search.worldbank.org/api/v2/procnotices");
    private const int PageSize = 20;
    private const int MaxResponseBytes = 6 * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public CrmWorldBankProcurementProvider(HttpClient httpClient)
        => _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public string ProviderCode => "world_bank_procurement";

    public void Dispose() => _httpClient.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(
        CrmProspectingProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var keywords = ExtractKeywords(request);
        if (keywords.Count == 0)
        {
            return new CrmProspectingProviderResult(
                [],
                0,
                ["World Bank Procurement requires meaningful product or industry keywords."])
            {
                IsComplete = false
            };
        }

        var query = new UriBuilder(Endpoint);
        var parameters = new Dictionary<string, string>
        {
            ["format"] = "json",
            ["rows"] = PageSize.ToString(CultureInfo.InvariantCulture),
            ["os"] = "0",
            ["qterm"] = string.Join(' ', keywords.Concat(ExtractCountryTerms(request)).Distinct(StringComparer.OrdinalIgnoreCase))
        };
        query.Query = string.Join("&", parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        using var message = new HttpRequestMessage(HttpMethod.Get, query.Uri);
        message.Headers.Accept.ParseAdd("application/json");
        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
            throw new HttpRequestException("World Bank Procurement response came from an unapproved endpoint.");

        if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            var retryAt = response.Headers.RetryAfter?.Date
                ?? (response.Headers.RetryAfter?.Delta is { } retryDelay
                    ? DateTimeOffset.UtcNow + retryDelay
                    : DateTimeOffset.UtcNow.AddMinutes(5));
            throw new CrmProspectingProviderRateLimitException(
                "World Bank Procurement throttled the request; retry after the supplied cooldown.",
                (int)response.StatusCode,
                retryAt);
        }

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("World Bank Procurement returned an HTTP error.", null, response.StatusCode);

        var payload = await ReadBoundedAsync(response, MaxResponseBytes, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("procnotices", out var notices)
            || notices.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("World Bank Procurement response did not contain a valid procnotices array.");

        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        var retrievedAt = DateTimeOffset.UtcNow;
        foreach (var notice in notices.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = ReadString(notice, "id");
            var description = FirstNonEmpty(
                ReadString(notice, "bid_description"),
                ReadString(notice, "project_name"),
                "World Bank procurement notice");
            var buyer = FirstNonEmpty(
                ReadString(notice, "contact_organization"),
                ReadString(notice, "project_name"));
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(buyer))
            {
                warnings.Add("A World Bank notice without a stable ID or buyer organization was skipped.");
                continue;
            }

            var projectId = ReadString(notice, "project_id");
            var bidReference = ReadString(notice, "bid_reference_no");
            var noticeType = ReadString(notice, "notice_type");
            var deadline = ReadString(notice, "submission_deadline_date");
            var country = FirstNonEmpty(
                ReadString(notice, "contact_ctry_name"),
                ReadString(notice, "project_ctry_name"));
            var contact = ReadString(notice, "contact_name");
            var email = NormalizeEmail(ReadString(notice, "contact_email"));
            var phone = ReadString(notice, "contact_phone_no");
            var summary = string.Join("; ", new[]
            {
                string.IsNullOrWhiteSpace(projectId) ? null : $"Project ID: {projectId}",
                string.IsNullOrWhiteSpace(bidReference) ? null : $"Bid reference: {bidReference}",
                string.IsNullOrWhiteSpace(noticeType) ? null : $"Notice type: {noticeType}",
                string.IsNullOrWhiteSpace(deadline) ? null : $"Submission deadline: {deadline}",
                string.IsNullOrWhiteSpace(phone) ? null : $"Public contact phone: {phone}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            records.Add(new CrmProspectingRawRecord(
                $"WORLD_BANK:{id}",
                "World Bank Procurement",
                Limit(description, 500),
                Limit(buyer, 200),
                null,
                Limit(contact, 200),
                email,
                Limit(summary, 2000))
            {
                SourceUrl = $"https://projects.worldbank.org/en/projects-operations/procurement-detail/{Uri.EscapeDataString(id)}",
                PublishedAtUtc = ParseDate(FirstNonEmpty(ReadString(notice, "noticedate"), ReadString(notice, "notice_date"))),
                RetrievedAtUtc = retrievedAt,
                RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(notice.GetRawText()))).ToLowerInvariant(),
                EvidenceType = "official_procurement"
            });
        }

        var total = ReadInt64(document.RootElement, "total");
        var hasMore = total > notices.GetArrayLength();
        if (hasMore)
            warnings.Add($"World Bank Procurement returned one bounded page ({PageSize} max); {total} total notices were reported. Coverage is incomplete because the current provider contract does not expose a page cursor.");

        return new CrmProspectingProviderResult(records, 0, warnings)
        {
            IsComplete = !hasMore
        };
    }

    private static List<string> ExtractKeywords(CrmProspectingProviderRequest request)
        => new[] { request.ProductScope, request.SearchObjective, request.IdealCustomerProfile }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => Regex.Split(value!, @"[^\p{L}\p{N}]+"))
            .Select(value => value.Trim())
            .Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    private static IEnumerable<string> ExtractCountryTerms(CrmProspectingProviderRequest request)
        => Regex.Split(request.TargetMarkets ?? string.Empty, @"[,;，；|]+")
            .Select(value => value.Trim())
            .Where(value => value.Length >= 2)
            .Take(3);

    private static async Task<byte[]> ReadBoundedAsync(
        HttpResponseMessage response,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > maxBytes)
            throw new InvalidDataException("World Bank Procurement response exceeded the 6 MB limit.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maxBytes)
                throw new InvalidDataException("World Bank Procurement response exceeded the 6 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static long ReadInt64(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
        return 0;
    }

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private static string? NormalizeEmail(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Contains('@', StringComparison.Ordinal)
            ? value.Trim()
            : null;

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string Limit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
