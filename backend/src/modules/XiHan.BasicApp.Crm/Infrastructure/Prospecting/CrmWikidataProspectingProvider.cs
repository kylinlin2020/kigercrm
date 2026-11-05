using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Low-volume Wikidata entity discovery. Results are knowledge-graph clues only:
/// they are not authoritative company registrations or evidence of buyer intent.
/// </summary>
public sealed class CrmWikidataProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://www.wikidata.org/w/api.php");
    private const int MaxTerms = 3;
    private const int PageSize = 10;
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private readonly HttpClient _client;

    public CrmWikidataProspectingProvider(HttpClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    public string ProviderCode => "wikidata";
    public void Dispose() => _client.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(
        CrmProspectingProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var terms = Regex.Split($"{request.ProductScope} {request.SearchObjective} {request.IdealCustomerProfile}", @"[^\p{L}\p{N}]+")
            .Where(term => term.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .ToArray();
        if (terms.Length == 0)
            return new CrmProspectingProviderResult([], 0, ["Wikidata search requires product or industry keywords."])
            { IsComplete = false };

        var records = new List<CrmProspectingRawRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var complete = true;
        var retrievedAt = DateTimeOffset.UtcNow;

        foreach (var term in terms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = new Dictionary<string, string>
            {
                ["action"] = "wbsearchentities",
                ["search"] = term,
                ["language"] = "en",
                ["uselang"] = "en",
                ["type"] = "item",
                ["limit"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["format"] = "json"
            };
            var uri = new UriBuilder(Endpoint)
            {
                Query = string.Join("&", query.Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
            }.Uri;
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Accept.ParseAdd("application/json");
            message.Headers.UserAgent.ParseAdd("KigerCRM-Prospecting/1.0 (https://kin-power.com)");

            using var response = await _client.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
                throw new HttpRequestException("Wikidata response came from an unapproved endpoint.");

            if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
            {
                var retryAt = response.Headers.RetryAfter?.Date
                    ?? (response.Headers.RetryAfter?.Delta is { } delay
                        ? DateTimeOffset.UtcNow + delay
                        : DateTimeOffset.UtcNow.AddMinutes(5));
                throw new CrmProspectingProviderRateLimitException(
                    "Wikidata throttled the entity search; stop requests and retry after the supplied cooldown.",
                    (int)response.StatusCode, retryAt);
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Wikidata returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);

            using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
            if (!document.RootElement.TryGetProperty("search", out var search) || search.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Wikidata response did not contain a search array.");

            if (search.GetArrayLength() >= PageSize)
            {
                complete = false;
                warnings.Add($"Wikidata returned the {PageSize}-item cap for keyword '{Limit(term, 60)}'; additional matching entities may exist.");
            }

            foreach (var item in search.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = Read(item, "id");
                var label = Read(item, "label");
                var description = Read(item, "description");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label) || !IsOrganizationLike(description))
                    continue;
                if (!Regex.IsMatch(id, @"^Q[1-9][0-9]*$", RegexOptions.CultureInvariant) || !seen.Add(id))
                    continue;

                records.Add(new CrmProspectingRawRecord(
                    $"WIKIDATA:{id}",
                    "Wikidata",
                    Limit(label, 500),
                    Limit(label, 200),
                    null,
                    null,
                    null,
                    Limit(description, 1000))
                {
                    SourceUrl = $"https://www.wikidata.org/wiki/{id}",
                    RetrievedAtUtc = retrievedAt,
                    RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                    EvidenceType = "knowledge_graph_entity"
                });
            }
        }

        if (records.Count == 0)
            warnings.Add("No organization-like Wikidata entity descriptions matched the requested keywords; this does not establish that no companies exist.");

        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static bool IsOrganizationLike(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return false;
        return Regex.IsMatch(description,
            @"\b(company|companies|corporation|business|enterprise|manufacturer|manufacturer of|supplier|distributor|organization|organisation|firm|factory|producer)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string? Read(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Limit(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
            throw new InvalidDataException("Wikidata response exceeded the 2 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidDataException("Wikidata response exceeded the 2 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
