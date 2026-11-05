using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>Serper Google organic-search adapter; results are web-discovery evidence, not verified company records.</summary>
public sealed class CrmSerperSearchProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://google.serper.dev/search");
    private const int ResultCount = 20;
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<string?>> _key;

    public CrmSerperSearchProspectingProvider(HttpClient client, string? apiKey)
        : this(client, _ => Task.FromResult(string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim())) { }

    public CrmSerperSearchProspectingProvider(HttpClient client, ISaasConfigurationService configuration)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(configuration);
        _key = ct => configuration.GetStringAsync("CRM.Prospecting.Provider.serper.ApiKey", cancellationToken: ct);
    }

    private CrmSerperSearchProspectingProvider(HttpClient client, Func<CancellationToken, Task<string?>> key)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _key = key;
    }

    public string ProviderCode => "serper";
    public void Dispose() => _client.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var apiKey = await _key(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
            return new CrmProspectingProviderResult([], 1, ["Serper requires a tenant API key. Configure it in the provider credentials panel."]) { IsComplete = false };

        var queryText = string.Join(" ", new[]
        {
            request.ProductScope, request.IdealCustomerProfile, request.SearchObjective, request.TargetMarkets
        }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        if (queryText.Length > 600) queryText = queryText[..600];
        if (queryText.Length == 0)
            return new CrmProspectingProviderResult([], 0, ["Serper requires a non-empty prospecting query."]) { IsComplete = false };

        var body = new Dictionary<string, object>
        {
            ["q"] = queryText,
            ["num"] = ResultCount
        };
        var country = ResolveCountry(request.TargetMarkets);
        if (country is not null) body["gl"] = country;
        else if (!string.IsNullOrWhiteSpace(request.TargetMarkets))
            body["hl"] = "en";

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Add("X-API-KEY", apiKey.Trim());
        message.Headers.Accept.ParseAdd("application/json");
        message.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
            throw new HttpRequestException("Serper response came from an unapproved endpoint.");
        if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            var retry = response.Headers.RetryAfter?.Date
                ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddMinutes(5));
            throw new CrmProspectingProviderRateLimitException("Serper throttled the request or exhausted available credits.", (int)response.StatusCode, retry);
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Serper returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);

        using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
            throw new InvalidDataException($"Serper returned an API error: {Limit(error.ValueKind == JsonValueKind.String ? error.GetString() : error.ToString(), 300)}");
        if (!root.TryGetProperty("organic", out var organic) || organic.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Serper response did not contain organic search results.");

        var records = new List<CrmProspectingRawRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retrievedAt = DateTimeOffset.UtcNow;
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
            records.Add(new CrmProspectingRawRecord($"SERPER:{hash}", "Serper", Limit(title, 500),
                null, $"{sourceUri.Scheme}://{sourceUri.IdnHost}/", null, null, Limit(snippet, 1800))
            {
                SourceUrl = canonical,
                RetrievedAtUtc = retrievedAt,
                RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                EvidenceType = "web_search_result"
            });
        }

        var complete = organic.GetArrayLength() < ResultCount;
        var warnings = new List<string>();
        if (!complete) warnings.Add($"Serper returned a full {ResultCount}-result page; additional results were not fetched by this bounded adapter.");
        if (records.Count == 0) warnings.Add("Serper returned no usable HTTPS organic results after excluded-domain filtering.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static string? ResolveCountry(string markets)
    {
        var first = markets.Split(new[] { ',', ';', '，', '；', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first)) return null;
        return first.ToUpperInvariant() switch
        {
            "US" or "USA" or "UNITED STATES" => "us", "UK" or "GB" or "UNITED KINGDOM" => "gb",
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
            throw new InvalidDataException("Serper response exceeded the 8 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes) throw new InvalidDataException("Serper response exceeded the 8 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
