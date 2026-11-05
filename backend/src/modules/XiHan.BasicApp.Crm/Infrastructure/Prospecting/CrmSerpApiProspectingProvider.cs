using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>SerpApi Google Search adapter. Its API contract requires api_key in the query string; diagnostics must redact the complete query.</summary>
public sealed class CrmSerpApiProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://serpapi.com/search");
    private const int PageSize = 10;
    private const int MaxPages = 2;
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<string?>> _key;

    public CrmSerpApiProspectingProvider(HttpClient client, string? apiKey)
        : this(client, _ => Task.FromResult(string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim())) { }

    public CrmSerpApiProspectingProvider(HttpClient client, ISaasConfigurationService configuration)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(configuration);
        _key = ct => configuration.GetStringAsync("CRM.Prospecting.Provider.serpapi.ApiKey", cancellationToken: ct);
    }

    private CrmSerpApiProspectingProvider(HttpClient client, Func<CancellationToken, Task<string?>> key)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _key = key;
    }

    public string ProviderCode => "serpapi";
    public void Dispose() => _client.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var apiKey = await _key(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
            return new CrmProspectingProviderResult([], 1, ["SerpApi requires a tenant API key. Configure it in the provider credentials panel."]) { IsComplete = false };

        var queryText = string.Join(" ", new[]
        {
            request.ProductScope, request.IdealCustomerProfile, request.SearchObjective, request.TargetMarkets
        }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        if (queryText.Length > 600) queryText = queryText[..600];
        if (queryText.Length == 0)
            return new CrmProspectingProviderResult([], 0, ["SerpApi requires a non-empty prospecting query."]) { IsComplete = false };

        var warnings = new List<string>
        {
            "SerpApi requires its API key in the request query string; ensure HTTP diagnostics redact the full URL query before logging."
        };
        var records = new List<CrmProspectingRawRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retrievedAt = DateTimeOffset.UtcNow;
        var country = ResolveCountry(request.TargetMarkets);
        var complete = true;
        var hasNext = false;

        for (var page = 0; page < MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = new Dictionary<string, string>
            {
                ["engine"] = "google",
                ["q"] = queryText,
                ["api_key"] = apiKey.Trim(),
                ["num"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["start"] = (page * PageSize).ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            if (country is not null) query["gl"] = country;
            query["hl"] = "en";
            var uri = new UriBuilder(Endpoint)
            {
                Query = string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
            }.Uri;
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Accept.ParseAdd("application/json");
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
                throw new HttpRequestException("SerpApi response came from an unapproved endpoint.");
            if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
            {
                var retry = response.Headers.RetryAfter?.Date
                    ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddMinutes(5));
                throw new CrmProspectingProviderRateLimitException("SerpApi throttled the request or exhausted available searches.", (int)response.StatusCode, retry);
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"SerpApi returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);

            using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new InvalidDataException($"SerpApi returned an API error: {Limit(error.ValueKind == JsonValueKind.String ? error.GetString() : error.ToString(), 300)}");
            if (!root.TryGetProperty("organic_results", out var organic) || organic.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("SerpApi response did not contain organic_results.");

            hasNext = root.TryGetProperty("serpapi_pagination", out var pagination)
                && pagination.ValueKind == JsonValueKind.Object
                && pagination.TryGetProperty("next", out var next)
                && next.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(next.GetString());

            foreach (var item in organic.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var title = Read(item, "title");
                var link = Read(item, "link");
                var snippet = Read(item, "snippet");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)
                    || !Uri.TryCreate(link, UriKind.Absolute, out var sourceUri)
                    || sourceUri.Scheme != Uri.UriSchemeHttps)
                    continue;
                if (request.ExcludedDomains.Any(domain => IsExcludedHost(sourceUri.Host, domain))) continue;
                var canonical = sourceUri.GetLeftPart(UriPartial.Path);
                if (!seen.Add(canonical)) continue;
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
                records.Add(new CrmProspectingRawRecord($"SERPAPI:{hash}", "SerpApi", Limit(title, 500),
                    null, $"{sourceUri.Scheme}://{sourceUri.IdnHost}/", null, null, Limit(snippet, 1800))
                {
                    SourceUrl = canonical,
                    RetrievedAtUtc = retrievedAt,
                    RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                    EvidenceType = "web_search_result"
                });
            }

            if (!hasNext) break;
            if (page == MaxPages - 1)
            {
                complete = false;
                warnings.Add($"SerpApi reports another page; stopped after {MaxPages} pages.");
            }
        }

        if (records.Count == 0) warnings.Add("SerpApi returned no usable HTTPS organic results after excluded-domain filtering.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static string? ResolveCountry(string markets)
    {
        var first = markets.Split(new[] { ',', ';', '，', '；', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first)) return null;
        return first.ToUpperInvariant() switch
        {
            "US" or "USA" or "UNITED STATES" => "us", "UK" or "GB" or "UNITED KINGDOM" => "uk",
            "NL" or "NLD" or "NETHERLANDS" or "HOLLAND" => "nl", "DE" or "GERMANY" => "de",
            "FR" or "FRANCE" => "fr", "ES" or "SPAIN" => "es", "IT" or "ITALY" => "it",
            "CA" or "CANADA" => "ca", "AU" or "AUSTRALIA" => "au", "BR" or "BRAZIL" => "br",
            "SG" or "SINGAPORE" => "sg", "JP" or "JAPAN" => "jp", "CN" or "CHINA" => "cn",
            "IN" or "INDIA" => "in", "MX" or "MEXICO" => "mx", _ => null
        };
    }

    private static bool IsExcludedHost(string host, string excluded)
    {
        var value = excluded.Trim().TrimStart('@', '.');
        return value.Length > 0 && (host.Equals(value, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + value, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Read(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Limit(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
            throw new InvalidDataException("SerpApi response exceeded the 8 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes) throw new InvalidDataException("SerpApi response exceeded the 8 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
