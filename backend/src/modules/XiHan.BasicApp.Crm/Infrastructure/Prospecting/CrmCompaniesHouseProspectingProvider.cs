using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

public sealed class CrmCompaniesHouseProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.company-information.service.gov.uk/search/companies");
    private const int PageSize = 100;
    private const int MaxPages = 2;
    private const int MaxResponseBytes = 6 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<string?>> _key;

    public CrmCompaniesHouseProspectingProvider(HttpClient client, string? apiKey)
        : this(client, _ => Task.FromResult(string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim())) { }

    public CrmCompaniesHouseProspectingProvider(HttpClient client, ISaasConfigurationService configuration)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(configuration);
        _key = ct => configuration.GetStringAsync("CRM.Prospecting.Provider.companies_house.ApiKey", cancellationToken: ct);
    }

    private CrmCompaniesHouseProspectingProvider(HttpClient client, Func<CancellationToken, Task<string?>> key)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _key = key;
    }

    public string ProviderCode => "companies_house";
    public void Dispose() => _client.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var markets = Regex.Split(request.TargetMarkets ?? "", @"[,;，；|]+").Select(x => x.Trim()).ToArray();
        if (!markets.Any(IsUk))
            return new CrmProspectingProviderResult([], 0, ["Companies House was skipped because the target market is not the United Kingdom."]) { IsComplete = true };

        var apiKey = await _key(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
            return new CrmProspectingProviderResult([], 1, ["Companies House requires a tenant API key. Configure it in the provider credentials panel."]) { IsComplete = false };

        var terms = Regex.Split($"{request.ProductScope} {request.SearchObjective} {request.IdealCustomerProfile}", @"[^\p{L}\p{N}]+")
            .Where(x => x.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        if (terms.Length == 0)
            return new CrmProspectingProviderResult([], 0, ["Companies House requires product or industry keywords."]) { IsComplete = false };

        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        var retrievedAt = DateTimeOffset.UtcNow;
        var total = -1;
        var complete = true;
        for (var page = 0; page < MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = new Dictionary<string, string>
            {
                ["q"] = string.Join(' ', terms),
                ["items_per_page"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["start_index"] = (page * PageSize).ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            var uri = new UriBuilder(Endpoint) { Query = string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}")) }.Uri;
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey.Trim()}:")));
            message.Headers.Accept.ParseAdd("application/json");
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
                throw new HttpRequestException("Companies House response came from an unapproved endpoint.");
            if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
            {
                var retry = response.Headers.RetryAfter?.Date ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddMinutes(5));
                throw new CrmProspectingProviderRateLimitException("Companies House throttled the request.", (int)response.StatusCode, retry);
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Companies House returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);

            using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Companies House response did not contain an items array.");
            if (root.TryGetProperty("total_results", out var totalElement) && totalElement.TryGetInt32(out var parsedTotal))
                total = parsedTotal;

            foreach (var item in items.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var number = Read(item, "company_number");
                var name = Read(item, "title");
                if (string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(name))
                {
                    warnings.Add("A Companies House result without a company number or name was skipped.");
                    continue;
                }
                var status = Read(item, "company_status");
                var kind = Read(item, "company_type");
                var description = Read(item, "description");
                var address = item.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
                var locality = Read(address, "locality");
                var postalCode = Read(address, "postal_code");
                var country = Read(address, "country");
                var summary = string.Join("; ", new[]
                {
                    string.IsNullOrWhiteSpace(status) ? null : $"Status: {status}",
                    string.IsNullOrWhiteSpace(kind) ? null : $"Type: {kind}",
                    string.IsNullOrWhiteSpace(description) ? null : $"Registry description: {description}",
                    string.IsNullOrWhiteSpace(locality) ? null : $"Locality: {locality}",
                    string.IsNullOrWhiteSpace(postalCode) ? null : $"Postal code: {postalCode}",
                    string.IsNullOrWhiteSpace(country) ? null : $"Registered office country: {country}"
                }.Where(x => !string.IsNullOrWhiteSpace(x)));
                records.Add(new CrmProspectingRawRecord($"COMPANIESHOUSE:{number}", "UK Companies House", Limit(name, 500), Limit(name, 200), null, null, null, Limit(summary, 2000))
                {
                    SourceUrl = $"https://find-and-update.company-information.service.gov.uk/company/{Uri.EscapeDataString(number)}",
                    RetrievedAtUtc = retrievedAt,
                    RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                    EvidenceType = "official_company_registry"
                });
            }

            var count = items.GetArrayLength();
            var processed = page * PageSize + count;
            if (count < PageSize || (total >= 0 && processed >= total)) break;
            if (page == MaxPages - 1)
            {
                complete = false;
                warnings.Add($"Companies House has more matching results; stopped after {MaxPages} pages.");
            }
        }
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static bool IsUk(string value)
        => value.Equals("UK", StringComparison.OrdinalIgnoreCase)
           || value.Equals("GB", StringComparison.OrdinalIgnoreCase)
           || value.Equals("United Kingdom", StringComparison.OrdinalIgnoreCase)
           || value.Equals("Great Britain", StringComparison.OrdinalIgnoreCase)
           || value.Equals("England", StringComparison.OrdinalIgnoreCase)
           || value.Equals("Scotland", StringComparison.OrdinalIgnoreCase)
           || value.Equals("Wales", StringComparison.OrdinalIgnoreCase)
           || value.Equals("Northern Ireland", StringComparison.OrdinalIgnoreCase);

    private static string? Read(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Limit(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
            throw new InvalidDataException("Companies House response exceeded the 6 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidDataException("Companies House response exceeded the 6 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
