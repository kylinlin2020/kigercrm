using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>Searches the official Dutch Chamber of Commerce register; records are identity evidence, not buyer intent.</summary>
public sealed class CrmKvkProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.kvk.nl/api/v2/zoeken");
    private const int PageSize = 100;
    private const int MaxPagesPerTerm = 2;
    private const int MaxTerms = 3;
    private const int MaxResponseBytes = 6 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<string?>> _key;

    public CrmKvkProspectingProvider(HttpClient client, string? apiKey)
        : this(client, _ => Task.FromResult(string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim())) { }

    public CrmKvkProspectingProvider(HttpClient client, ISaasConfigurationService configuration)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(configuration);
        _key = ct => configuration.GetStringAsync("CRM.Prospecting.Provider.kvk.ApiKey", cancellationToken: ct);
    }

    private CrmKvkProspectingProvider(HttpClient client, Func<CancellationToken, Task<string?>> key)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _key = key;
    }

    public string ProviderCode => "kvk";
    public void Dispose() => _client.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var markets = Regex.Split(request.TargetMarkets ?? "", @"[,;，；|]+").Select(x => x.Trim()).ToArray();
        if (!markets.Any(IsNetherlands))
            return new CrmProspectingProviderResult([], 0, ["KVK was skipped because the target market is not the Netherlands."]) { IsComplete = true };

        var apiKey = await _key(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
            return new CrmProspectingProviderResult([], 1, ["KVK requires a tenant API key and an active KVK API subscription."]) { IsComplete = false };

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "find", "search", "discover", "prospect", "prospecting", "lead", "leads", "target",
            "market", "company", "companies", "business", "businesses", "the", "and", "for",
            "with", "from", "into", "of", "in", "to", "a", "an", "netherlands", "dutch"
        };
        var terms = Regex.Split($"{request.ProductScope} {request.IdealCustomerProfile} {request.SearchObjective}", @"[^\p{L}\p{N}]+")
            .Where(x => x.Length >= 3 && !stopWords.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .ToArray();
        if (terms.Length == 0)
            return new CrmProspectingProviderResult([], 0, ["KVK search requires product or industry keywords."]) { IsComplete = false };

        var records = new List<CrmProspectingRawRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var complete = true;
        var retrievedAt = DateTimeOffset.UtcNow;

        foreach (var term in terms)
        {
            var total = -1;
            var processed = 0;
            for (var page = 1; page <= MaxPagesPerTerm; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = new Dictionary<string, string>
                {
                    ["naam"] = term,
                    ["pagina"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["resultatenPerPagina"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["inclusiefInactieveRegistraties"] = "false"
                };
                var uri = new UriBuilder(Endpoint)
                {
                    Query = string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"))
                }.Uri;
                using var message = new HttpRequestMessage(HttpMethod.Get, uri);
                message.Headers.Add("apikey", apiKey.Trim());
                message.Headers.Accept.ParseAdd("application/json");
                message.Headers.UserAgent.ParseAdd("KigerCRM-Prospecting/1.0 (https://kin-power.com)");

                using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
                    throw new HttpRequestException("KVK response came from an unapproved endpoint.");
                if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    var retry = response.Headers.RetryAfter?.Date
                        ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddMinutes(5));
                    throw new CrmProspectingProviderRateLimitException("KVK throttled the company-register search.", (int)response.StatusCode, retry);
                }
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"KVK returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);

                using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
                var root = document.RootElement;
                if (!TryGetResults(root, out var items))
                    throw new InvalidDataException("KVK response did not contain a resultaten array.");
                total = ReadInt(root, "totaal") ?? total;
                if (total < 0 && root.TryGetProperty("metadata", out var metadata))
                    total = ReadInt(metadata, "totaal") ?? total;

                foreach (var item in items.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var number = Read(item, "kvkNummer");
                    var name = Read(item, "naam");
                    if (string.IsNullOrWhiteSpace(number) || !Regex.IsMatch(number, @"^\d{8}$", RegexOptions.CultureInvariant)
                        || string.IsNullOrWhiteSpace(name))
                    {
                        warnings.Add("A KVK result without a valid eight-digit KVK number or name was skipped.");
                        continue;
                    }
                    if (!seen.Add(number)) continue;

                    var active = Read(item, "actief");
                    var type = Read(item, "type");
                    var branch = Read(item, "vestigingsnummer");
                    var address = ReadAddress(item);
                    var summary = string.Join("; ", new[]
                    {
                        string.IsNullOrWhiteSpace(active) ? null : $"Active registration: {active}",
                        string.IsNullOrWhiteSpace(type) ? null : $"Registration type: {type}",
                        string.IsNullOrWhiteSpace(branch) ? null : $"Branch number: {branch}",
                        string.IsNullOrWhiteSpace(address) ? null : $"Registered address: {address}"
                    }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    records.Add(new CrmProspectingRawRecord($"KVK:{number}", "Netherlands KVK Business Register",
                        Limit(name, 500), Limit(name, 200), null, null, null, Limit(summary, 1500))
                    {
                        RetrievedAtUtc = retrievedAt,
                        RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                        EvidenceType = "official_company_registry"
                    });
                }

                var count = items.GetArrayLength();
                processed += count;
                if (total >= 0 ? processed >= total : count < PageSize) break;
                if (page == MaxPagesPerTerm)
                {
                    complete = false;
                    warnings.Add($"KVK has more matches for keyword '{Limit(term, 60)}'; stopped after {MaxPagesPerTerm} pages.");
                }
            }
        }

        if (records.Count == 0)
            warnings.Add("KVK returned no usable registrations for the selected terms; this does not establish that no Dutch companies exist.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static bool IsNetherlands(string value)
        => value.Equals("NL", StringComparison.OrdinalIgnoreCase)
           || value.Equals("NLD", StringComparison.OrdinalIgnoreCase)
           || value.Equals("Netherlands", StringComparison.OrdinalIgnoreCase)
           || value.Equals("The Netherlands", StringComparison.OrdinalIgnoreCase)
           || value.Equals("Holland", StringComparison.OrdinalIgnoreCase)
           || value.Equals("Nederland", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetResults(JsonElement root, out JsonElement results)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("resultaten", out results) && results.ValueKind == JsonValueKind.Array)
            return true;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out results) && results.ValueKind == JsonValueKind.Array)
            return true;
        results = default;
        return false;
    }

    private static int? ReadInt(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
           && value.TryGetInt32(out var parsed) ? parsed : null;

    private static string? Read(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string? ReadAddress(JsonElement item)
    {
        if (!item.TryGetProperty("adres", out var address) || address.ValueKind != JsonValueKind.Object)
            return null;
        var parts = new[] { "straatnaam", "huisnummer", "postcode", "plaats", "land" }
            .Select(key => Read(address, key))
            .Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join(" ", parts);
    }

    private static string Limit(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
            throw new InvalidDataException("KVK response exceeded the 6 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidDataException("KVK response exceeded the 6 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
