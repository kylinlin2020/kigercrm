using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>Brave web-search adapter. Results are discovery evidence and are not verified company records.</summary>
public sealed class CrmBraveSearchProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.search.brave.com/res/v1/web/search");
    private const int PageSize = 20;
    private const int MaxPages = 2;
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<string?>> _key;

    public CrmBraveSearchProspectingProvider(HttpClient client, string? apiKey)
        : this(client, _ => Task.FromResult(string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim())) { }

    public CrmBraveSearchProspectingProvider(HttpClient client, ISaasConfigurationService configuration)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(configuration);
        _key = ct => configuration.GetStringAsync("CRM.Prospecting.Provider.brave.ApiKey", cancellationToken: ct);
    }

    private CrmBraveSearchProspectingProvider(HttpClient client, Func<CancellationToken, Task<string?>> key)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _key = key;
    }

    public string ProviderCode => "brave";
    public void Dispose() => _client.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var apiKey = await _key(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
            return new CrmProspectingProviderResult([], 1, ["Brave Search requires a tenant API subscription token. Configure it in the provider credentials panel."]) { IsComplete = false };

        var queryText = string.Join(" ", new[]
        {
            request.ProductScope,
            request.IdealCustomerProfile,
            request.SearchObjective,
            request.TargetMarkets
        }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        if (queryText.Length > 600) queryText = queryText[..600];
        if (queryText.Length == 0)
            return new CrmProspectingProviderResult([], 0, ["Brave Search requires a product, customer-profile, objective or market query."]) { IsComplete = false };

        var warnings = new List<string>();
        var records = new List<CrmProspectingRawRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retrievedAt = DateTimeOffset.UtcNow;
        var country = ResolveCountry(request.TargetMarkets);
        if (country is null)
            warnings.Add("Brave Search country targeting was not inferred from the supplied target market; the provider default country may apply.");

        var complete = true;
        var moreResults = false;
        for (var page = 0; page < MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = new Dictionary<string, string>
            {
                ["q"] = queryText,
                ["count"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["offset"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["safesearch"] = "moderate"
            };
            if (country is not null) query["country"] = country;
            var uri = new UriBuilder(Endpoint)
            {
                Query = string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
            }.Uri;
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Add("X-Subscription-Token", apiKey.Trim());
            message.Headers.Accept.ParseAdd("application/json");
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
                throw new HttpRequestException("Brave Search response came from an unapproved endpoint.");
            if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
            {
                var retry = response.Headers.RetryAfter?.Date
                    ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddMinutes(5));
                throw new CrmProspectingProviderRateLimitException("Brave Search throttled the request.", (int)response.StatusCode, retry);
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Brave Search returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);

            using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty("web", out var web) || web.ValueKind != JsonValueKind.Object
                || !web.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Brave Search response did not contain web.results.");

            var hasMoreFlag = root.TryGetProperty("query", out var queryMetadata)
                && queryMetadata.ValueKind == JsonValueKind.Object
                && queryMetadata.TryGetProperty("more_results_available", out var more)
                && more.ValueKind is JsonValueKind.True or JsonValueKind.False;
            moreResults = hasMoreFlag ? more.GetBoolean() : results.GetArrayLength() >= PageSize;
            if (!hasMoreFlag && results.GetArrayLength() >= PageSize)
                warnings.Add("Brave Search omitted more_results_available while returning a full page; coverage is treated as potentially incomplete.");

            foreach (var item in results.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var title = Read(item, "title");
                var urlText = Read(item, "url");
                var description = Read(item, "description");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(urlText)
                    || !Uri.TryCreate(urlText, UriKind.Absolute, out var sourceUri)
                    || sourceUri.Scheme != Uri.UriSchemeHttps)
                    continue;
                if (request.ExcludedDomains.Any(domain => IsExcludedHost(sourceUri.Host, domain)))
                    continue;

                var canonicalUrl = sourceUri.GetLeftPart(UriPartial.Path);
                if (!seen.Add(canonicalUrl)) continue;
                var host = sourceUri.IdnHost;
                var website = $"{sourceUri.Scheme}://{host}/";
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalUrl))).ToLowerInvariant();
                records.Add(new CrmProspectingRawRecord($"BRAVE:{hash}", "Brave Search", Limit(title, 500),
                    null, website, null, null, Limit(description, 1800))
                {
                    SourceUrl = canonicalUrl,
                    RetrievedAtUtc = retrievedAt,
                    RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                    EvidenceType = "web_search_result"
                });
            }

            if (!moreResults) break;
            if (page == MaxPages - 1)
            {
                complete = false;
                warnings.Add($"Brave Search reports more results; stopped after {MaxPages} pages.");
            }
        }

        if (records.Count == 0)
            warnings.Add("Brave Search returned no usable HTTPS web results after excluded-domain filtering.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static string? ResolveCountry(string targetMarkets)
    {
        var first = targetMarkets.Split(new[] { ',', ';', '，', '；', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first)) return null;
        return first.Trim().ToUpperInvariant() switch
        {
            "US" or "USA" or "UNITED STATES" => "US",
            "UK" or "GB" or "UNITED KINGDOM" or "GREAT BRITAIN" => "GB",
            "NL" or "NLD" or "NETHERLANDS" or "HOLLAND" or "NEDERLAND" => "NL",
            "DE" or "GERMANY" or "DEUTSCHLAND" => "DE",
            "FR" or "FRANCE" => "FR",
            "ES" or "SPAIN" => "ES",
            "IT" or "ITALY" => "IT",
            "CA" or "CANADA" => "CA",
            "AU" or "AUSTRALIA" => "AU",
            "BR" or "BRAZIL" => "BR",
            "SG" or "SINGAPORE" => "SG",
            "JP" or "JAPAN" => "JP",
            "CN" or "CHINA" => "CN",
            "IN" or "INDIA" => "IN",
            "MX" or "MEXICO" => "MX",
            "ZA" or "SOUTH AFRICA" => "ZA",
            _ => null
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
            throw new InvalidDataException("Brave Search response exceeded the 8 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidDataException("Brave Search response exceeded the 8 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
