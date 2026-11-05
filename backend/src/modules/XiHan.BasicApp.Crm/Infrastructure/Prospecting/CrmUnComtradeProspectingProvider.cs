using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// UN Comtrade trade-statistics preview adapter. Statistics are market signals, not company/contact identities.
/// The public preview endpoint is keyless and bounded to 500 records per request.
/// </summary>
public sealed class CrmUnComtradeProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri PublicEndpoint = new("https://comtradeapi.un.org/public/v1/preview/C/A/HS");
    private static readonly Uri KeyedEndpoint = new("https://comtradeapi.un.org/data/v1/get/C/A/HS");
    private const int MaxRecords = 500;
    private const int MaxResponseBytes = 6 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly Func<CancellationToken, Task<string?>> _subscriptionKeyFactory;

    public CrmUnComtradeProspectingProvider(HttpClient httpClient, string? subscriptionKey = null)
        : this(httpClient, _ => Task.FromResult(string.IsNullOrWhiteSpace(subscriptionKey) ? null : subscriptionKey.Trim())) { }

    public CrmUnComtradeProspectingProvider(HttpClient httpClient, ISaasConfigurationService configuration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(configuration);
        _subscriptionKeyFactory = cancellationToken => configuration.GetStringAsync(
            "CRM.Prospecting.Provider.un_comtrade.ApiKey", cancellationToken: cancellationToken);
    }

    private CrmUnComtradeProspectingProvider(HttpClient httpClient, Func<CancellationToken, Task<string?>> keyFactory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _subscriptionKeyFactory = keyFactory;
    }

    public string ProviderCode => "un_comtrade";
    public void Dispose() => _httpClient.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var reporter = ResolveReporterCode(request.TargetMarkets);
        if (reporter is null)
            return new CrmProspectingProviderResult([], 0, ["UN Comtrade requires a supported target-market country to select a reporter."]) { IsComplete = false };

        var keywords = ExtractKeywords(request);
        if (keywords.Count == 0)
            return new CrmProspectingProviderResult([], 0, ["UN Comtrade requires product or industry keywords."]) { IsComplete = false };

        var subscriptionKey = await _subscriptionKeyFactory(cancellationToken);
        subscriptionKey = string.IsNullOrWhiteSpace(subscriptionKey) ? null : subscriptionKey.Trim();
        var endpoint = subscriptionKey is null ? PublicEndpoint : KeyedEndpoint;
        var query = new Dictionary<string, string>
        {
            ["reporterCode"] = reporter,
            ["partnerCode"] = "0",
            ["period"] = (DateTime.UtcNow.Year - 1).ToString(CultureInfo.InvariantCulture),
            ["flowCode"] = "X",
            ["cmdCode"] = "TOTAL",
            ["maxRecords"] = MaxRecords.ToString(CultureInfo.InvariantCulture)
        };
        var builder = new UriBuilder(endpoint)
        {
            Query = string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
        };

        using var message = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
        message.Headers.Accept.ParseAdd("application/json");
        if (subscriptionKey is not null)
            message.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", subscriptionKey);
        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, endpoint))
            throw new HttpRequestException("UN Comtrade response came from an unapproved endpoint.");
        if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            var retryAt = response.Headers.RetryAfter?.Date
                ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddMinutes(5));
            throw new CrmProspectingProviderRateLimitException("UN Comtrade throttled the request; retry after the supplied cooldown.", (int)response.StatusCode, retryAt);
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("UN Comtrade returned an HTTP error.", null, response.StatusCode);

        using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("UN Comtrade response did not contain a valid data array.");

        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        var retrievedAt = DateTimeOffset.UtcNow;
        foreach (var item in data.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var commodity = ReadString(item, "cmdDesc") ?? ReadString(item, "cmdCode") ?? "Total commodities";
            if (!keywords.Any(term => commodity.Contains(term, StringComparison.OrdinalIgnoreCase))
                && !keywords.Any(term => request.ProductScope.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                // A TOTAL aggregate has no product-level classification; keep it as broad market context.
                if (!string.Equals(ReadString(item, "cmdCode"), "TOTAL", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var period = ReadString(item, "period") ?? query["period"];
            var reporterName = ReadString(item, "reporterDesc") ?? request.TargetMarkets;
            var partnerName = ReadString(item, "partnerDesc") ?? "World";
            var flow = ReadString(item, "flowDesc") ?? "Export";
            var value = ReadNumber(item, "primaryValue");
            var id = $"UNCOMTRADE:{reporter}:{period}:{flow}:{ReadString(item, "cmdCode") ?? "TOTAL"}";
            var summary = $"Reporter {reporterName}; partner {partnerName}; period {period}; {flow}; trade value {value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "not reported"}";
            records.Add(new CrmProspectingRawRecord(
                id,
                "UN Comtrade",
                Limit($"Trade signal: {reporterName} {flow.ToLowerInvariant()} activity for {request.ProductScope}", 500),
                null,
                null,
                null,
                null,
                Limit(summary, 2000))
            {
                SourceUrl = $"https://comtradeapi.un.org/{(subscriptionKey is null ? "public/v1/preview/C/A/HS" : "data/v1/get/C/A/HS")}?reporterCode={Uri.EscapeDataString(reporter)}&period={Uri.EscapeDataString(period)}&partnerCode=0&flowCode=X&cmdCode=TOTAL",
                RetrievedAtUtc = retrievedAt,
                RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                EvidenceType = "official_trade_statistics"
            });
        }

        var isPublicPreview = subscriptionKey is null;
        if (isPublicPreview)
            warnings.Add("UN Comtrade public preview is keyless and limited to 500 rows per call; use a subscription key for the registered data API and higher quotas.");
        var complete = data.GetArrayLength() < MaxRecords;
        if (!complete)
            warnings.Add("UN Comtrade reached the 500-record request cap; market coverage is incomplete.");
        if (records.Count == 0)
            warnings.Add("No trade-statistics rows matched this search scope; this is not evidence that no trade exists.");

        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static string? ResolveReporterCode(string markets)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["United States"] = "842", ["USA"] = "842", ["US"] = "842",
            ["Nigeria"] = "566", ["Brazil"] = "76", ["China"] = "156",
            ["United Kingdom"] = "826", ["UK"] = "826", ["Singapore"] = "702",
            ["Netherlands"] = "528", ["France"] = "250", ["Germany"] = "276",
            ["India"] = "356", ["Japan"] = "392", ["Canada"] = "124",
            ["South Africa"] = "710", ["Ghana"] = "288", ["Kenya"] = "404",
            ["United Arab Emirates"] = "784", ["UAE"] = "784", ["Turkey"] = "792",
            ["Türkiye"] = "792", ["Australia"] = "36", ["Mexico"] = "484"
        };
        foreach (var part in Regex.Split(markets ?? string.Empty, @"[,;，；|]+"))
        {
            var candidate = part.Trim();
            if (map.TryGetValue(candidate, out var code)) return code;
        }
        return null;
    }

    private static List<string> ExtractKeywords(CrmProspectingProviderRequest request)
        => Regex.Split($"{request.ProductScope} {request.SearchObjective} {request.IdealCustomerProfile}", @"[^\p{L}\p{N}]+")
            .Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    private static string? ReadString(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()
            : item.TryGetProperty(property, out value) && value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;

    private static decimal? ReadNumber(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int limit = MaxResponseBytes;
        if (response.Content.Headers.ContentLength is long length && length > limit)
            throw new InvalidDataException("UN Comtrade response exceeded the 6 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > limit) throw new InvalidDataException("UN Comtrade response exceeded the 6 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
}
