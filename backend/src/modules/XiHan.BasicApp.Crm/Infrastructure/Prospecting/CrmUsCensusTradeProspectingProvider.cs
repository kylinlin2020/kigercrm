using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// U.S. Census international trade statistics adapter. A Census API key is required for requests.
/// Returned records are country/commodity statistics, not company identities or leads.
/// </summary>
public sealed class CrmUsCensusTradeProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.census.gov/data/timeseries/intltrade/exports/hs");
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private const int MaxRecords = 2000;
    private readonly HttpClient _httpClient;
    private readonly Func<CancellationToken, Task<string?>> _apiKeyFactory;

    public CrmUsCensusTradeProspectingProvider(HttpClient httpClient, string? apiKey)
        : this(httpClient, _ => Task.FromResult(string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim())) { }

    public CrmUsCensusTradeProspectingProvider(HttpClient httpClient, ISaasConfigurationService configuration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(configuration);
        _apiKeyFactory = cancellationToken => configuration.GetStringAsync(
            "CRM.Prospecting.Provider.us_census.ApiKey", cancellationToken: cancellationToken);
    }

    private CrmUsCensusTradeProspectingProvider(HttpClient httpClient, Func<CancellationToken, Task<string?>> keyFactory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKeyFactory = keyFactory;
    }

    public string ProviderCode => "us_census_trade";
    public void Dispose() => _httpClient.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var apiKey = await _apiKeyFactory(cancellationToken);
        apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        if (apiKey is null)
            return new CrmProspectingProviderResult([], 1, ["US Census trade API requires a tenant API key. Configure it in the prospecting provider credentials panel."]) { IsComplete = false };

        var targetMarkets = Regex.Split(request.TargetMarkets ?? string.Empty, @"[,;，；|]+").Select(value => value.Trim()).Where(value => value.Length >= 2).ToArray();
        if (targetMarkets.Length == 0)
            return new CrmProspectingProviderResult([], 0, ["US Census trade search requires explicit target-market countries."]) { IsComplete = false };

        var keywords = Regex.Split($"{request.ProductScope} {request.SearchObjective} {request.IdealCustomerProfile}", @"[^\p{L}\p{N}]+")
            .Where(value => value.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        if (keywords.Count == 0)
            return new CrmProspectingProviderResult([], 0, ["US Census trade search requires product or industry keywords."]) { IsComplete = false };

        // Census trade data is published with a delay; use the month two months behind UTC to avoid querying an unfinished period.
        var period = DateTime.UtcNow.AddMonths(-2).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var query = new Dictionary<string, string>
        {
            ["get"] = "CTY_NAME,CTY_CODE,ALL_VAL_MO,E_COMMODITY,E_COMMODITY_LDESC",
            ["time"] = period,
            ["COMM_LVL"] = "HS2",
            ["key"] = apiKey
        };
        var builder = new UriBuilder(Endpoint)
        {
            Query = string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
        };
        using var message = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
        message.Headers.Accept.ParseAdd("application/json");
        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
            throw new HttpRequestException("US Census response came from an unapproved endpoint.");
        if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            var retryAt = response.Headers.RetryAfter?.Date
                ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddMinutes(5));
            throw new CrmProspectingProviderRateLimitException("US Census throttled the request; retry after the supplied cooldown.", (int)response.StatusCode, retryAt);
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("US Census returned an HTTP error.", null, response.StatusCode);

        using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 1 || root[0].ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("US Census response did not contain the expected header/data arrays.");
        var headers = root[0].EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        var retrievedAt = DateTimeOffset.UtcNow;
        foreach (var row in root.EnumerateArray().Skip(1).Take(MaxRecords))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.ValueKind != JsonValueKind.Array) continue;
            var values = row.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText()).ToArray();
            string Get(string name)
            {
                var index = Array.IndexOf(headers, name);
                return index >= 0 && index < values.Length ? values[index] : string.Empty;
            }
            var commodity = Get("E_COMMODITY_LDESC");
            if (string.IsNullOrWhiteSpace(commodity)) commodity = Get("E_COMMODITY");
            if (!keywords.Any(term => commodity.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
            var country = Get("CTY_NAME");
            var countryCode = Get("CTY_CODE");
            if (string.IsNullOrWhiteSpace(country) || string.IsNullOrWhiteSpace(countryCode)) continue;
            if (!targetMarkets.Any(target => country.Contains(target, StringComparison.OrdinalIgnoreCase)
                || target.Contains(country, StringComparison.OrdinalIgnoreCase))) continue;
            var allValue = Get("ALL_VAL_MO");
            var id = $"USCENSUS:EXPORT:{period}:{countryCode}:{Get("E_COMMODITY")}";
            var summary = $"U.S. exports to {country}; HS commodity {commodity}; month {period}; total value {allValue}";
            var canonical = $"https://api.census.gov/data/timeseries/intltrade/exports/hs?time={Uri.EscapeDataString(period)}&COMM_LVL=HS2";
            records.Add(new CrmProspectingRawRecord(
                id,
                "US Census International Trade",
                $"U.S. export trade signal: {commodity}",
                null,
                null,
                null,
                null,
                Limit(summary, 2000))
            {
                SourceUrl = canonical,
                RetrievedAtUtc = retrievedAt,
                RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.GetRawText()))).ToLowerInvariant(),
                EvidenceType = "official_trade_statistics"
            });
        }
        var truncated = root.GetArrayLength() - 1 > MaxRecords;
        if (truncated) warnings.Add($"US Census response exceeded the adapter's {MaxRecords}-row processing limit; coverage is incomplete.");
        if (records.Count == 0) warnings.Add("No US Census trade rows matched the product keywords for the selected month.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = !truncated };
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
            throw new InvalidDataException("US Census response exceeded the 8 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes) throw new InvalidDataException("US Census response exceeded the 8 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
}
