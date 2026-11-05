using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// FDA 510(k) clearance records are medical-device regulatory evidence, not general company
/// registration or proof of purchasing intent. Only medical-device campaigns may query this provider.
/// </summary>
public sealed class CrmOpenFda510kProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.fda.gov/device/510k.json");
    private const int PageSize = 100;
    private const int MaxPages = 3;
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<string?>> _apiKeyFactory;

    public CrmOpenFda510kProspectingProvider(HttpClient client, string? apiKey)
        : this(client, _ => Task.FromResult(string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim())) { }

    public CrmOpenFda510kProspectingProvider(HttpClient client, ISaasConfigurationService configuration)
        : this(client, cancellationToken => configuration.GetStringAsync(
            "CRM.Prospecting.Provider.open_fda_510k.ApiKey", cancellationToken: cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(configuration);
    }

    private CrmOpenFda510kProspectingProvider(HttpClient client, Func<CancellationToken, Task<string?>> keyFactory)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _apiKeyFactory = keyFactory ?? throw new ArgumentNullException(nameof(keyFactory));
    }

    public string ProviderCode => "open_fda_510k";
    public void Dispose() => _client.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(
        CrmProspectingProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsUnitedStatesTarget(request.TargetMarkets))
            return new CrmProspectingProviderResult([], 0,
                ["openFDA 510(k) was skipped because the target market does not include the United States."])
                { IsComplete = true };

        if (!IsMedicalDeviceCampaign(request))
            return new CrmProspectingProviderResult([], 0,
                ["openFDA 510(k) is restricted to campaigns explicitly targeting medical devices or medical equipment."])
                { IsComplete = true };

        var terms = Regex.Split($"{request.ProductScope} {request.SearchObjective} {request.IdealCustomerProfile}", @"[^\p{L}\p{N}]+")
            .Where(value => value.Length >= 3)
            .Where(value => !IsGenericTerm(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
        if (terms.Length == 0)
            return new CrmProspectingProviderResult([], 0, ["openFDA 510(k) requires a specific medical-device keyword."])
                { IsComplete = false };

        var apiKey = await _apiKeyFactory(cancellationToken);
        apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        var records = new List<CrmProspectingRawRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var complete = true;
        var retrievedAt = DateTimeOffset.UtcNow;

        foreach (var term in terms)
        {
            long total = 0;
            var hasTotal = false;
            var processed = 0;
            for (var page = 0; page < MaxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = new Dictionary<string, string>
                {
                    ["search"] = $"device_name:\"{EscapeSearchTerm(term)}\"",
                    ["limit"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["skip"] = (page * PageSize).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["sort"] = "decision_date:desc"
                };
                if (apiKey is not null) query["api_key"] = apiKey;
                var uri = new UriBuilder(Endpoint)
                {
                    Query = string.Join("&", query.Select(pair =>
                        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
                }.Uri;
                using var message = new HttpRequestMessage(HttpMethod.Get, uri);
                message.Headers.Accept.ParseAdd("application/json");
                using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
                    throw new HttpRequestException("openFDA response came from an unapproved endpoint.");
                if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    var retryAt = response.Headers.RetryAfter?.Date
                        ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddMinutes(5));
                    throw new CrmProspectingProviderRateLimitException(
                        "openFDA throttled the 510(k) search; retry after the supplied cooldown.",
                        (int)response.StatusCode, retryAt);
                }
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    break;
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("openFDA 510(k) API returned an HTTP error.", null, response.StatusCode);

                using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
                var root = document.RootElement;
                if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("openFDA 510(k) response did not contain a results array.");
                if (root.TryGetProperty("meta", out var meta)
                    && meta.TryGetProperty("results", out var metaResults)
                    && metaResults.TryGetProperty("total", out var totalElement)
                    && totalElement.TryGetInt64(out var parsedTotal))
                {
                    total = parsedTotal;
                    hasTotal = true;
                }

                foreach (var item in results.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var kNumber = Read(item, "k_number");
                    var deviceName = Read(item, "device_name");
                    var applicant = Read(item, "applicant");
                    if (string.IsNullOrWhiteSpace(kNumber) || string.IsNullOrWhiteSpace(deviceName))
                    {
                        warnings.Add("An openFDA record without a 510(k) number or device name was skipped.");
                        continue;
                    }
                    if (!seen.Add(kNumber)) continue;
                    var decisionDate = Read(item, "decision_date");
                    var productCode = Read(item, "product_code");
                    var summary = string.Join("; ", new[]
                    {
                        string.IsNullOrWhiteSpace(productCode) ? null : $"FDA product code {productCode}",
                        string.IsNullOrWhiteSpace(decisionDate) ? null : $"Decision date {decisionDate}",
                        "510(k) clearance record; not evidence of buyer intent"
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    records.Add(new CrmProspectingRawRecord(
                        $"FDA510K:{kNumber}",
                        "openFDA 510(k) Clearances",
                        Limit(deviceName, 500),
                        Limit(applicant, 200),
                        null,
                        null,
                        null,
                        Limit(summary, 2000))
                    {
                        SourceUrl = $"https://www.accessdata.fda.gov/scripts/cdrh/cfdocs/cfpmn/pmn.cfm?ID={Uri.EscapeDataString(kNumber)}",
                        PublishedAtUtc = ParseDate(decisionDate),
                        RetrievedAtUtc = retrievedAt,
                        RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                        EvidenceType = "medical_device_regulatory_record"
                    });
                }

                processed += results.GetArrayLength();
                if ((hasTotal && processed >= total) || results.GetArrayLength() < PageSize) break;
            }

            if (processed >= MaxPages * PageSize && (!hasTotal || total > processed))
            {
                complete = false;
                warnings.Add($"openFDA has {total} matching records for '{term}'; only {processed} were read.");
            }
        }

        if (records.Count == 0) warnings.Add("openFDA returned no matching 510(k) records for the selected medical-device keywords.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static bool IsUnitedStatesTarget(string markets)
        => Regex.Split(markets ?? string.Empty, @"[,;，；|]+").Select(value => value.Trim())
            .Any(value => value.Equals("United States", StringComparison.OrdinalIgnoreCase)
                || value.Equals("USA", StringComparison.OrdinalIgnoreCase)
                || value.Equals("US", StringComparison.OrdinalIgnoreCase)
                || value.Equals("United States of America", StringComparison.OrdinalIgnoreCase));

    private static bool IsMedicalDeviceCampaign(CrmProspectingProviderRequest request)
    {
        var scope = $"{request.ProductScope} {request.IdealCustomerProfile} {request.SearchObjective}";
        return Regex.IsMatch(scope,
            @"\b(medical|healthcare|clinical|diagnostic|surgical|hospital|medical device|medical equipment|510k|510\(k\))\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsGenericTerm(string value)
        => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "find", "search", "discover", "prospect", "prospecting", "lead", "leads", "company", "companies", "business", "businesses", "medical", "device", "devices", "equipment", "the", "and", "for", "with", "from", "into", "of", "in", "to" }.Contains(value);

    private static string EscapeSearchTerm(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    private static string? Read(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParseExact(value, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToUniversalTime() : null;
    private static string Limit(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= max ? value : value[..max];

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
            throw new InvalidDataException("openFDA response exceeded the 8 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes) throw new InvalidDataException("openFDA response exceeded the 8 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
