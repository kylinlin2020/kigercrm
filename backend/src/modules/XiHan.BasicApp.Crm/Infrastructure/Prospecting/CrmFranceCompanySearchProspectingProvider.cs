using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>Searches the open French government business directory; results are legal-identity evidence, not buyer intent.</summary>
public sealed class CrmFranceCompanySearchProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://recherche-entreprises.api.gouv.fr/search");
    private const int PageSize = 25;
    private const int MaxPagesPerTerm = 2;
    private const int MaxTerms = 3;
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private readonly HttpClient _client;

    public CrmFranceCompanySearchProspectingProvider(HttpClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    public string ProviderCode => "fr_company_search";
    public void Dispose() => _client.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var markets = Regex.Split(request.TargetMarkets ?? "", @"[,;，；|]+").Select(x => x.Trim()).ToArray();
        if (!markets.Any(IsFrance))
            return new CrmProspectingProviderResult([], 0, ["French company search was skipped because the target market is not France."]) { IsComplete = true };

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "find", "search", "discover", "prospect", "prospecting", "lead", "leads", "target",
            "market", "company", "companies", "business", "businesses", "the", "and", "for",
            "with", "from", "into", "of", "in", "to", "a", "an", "france", "french"
        };
        var terms = Regex.Split($"{request.ProductScope} {request.IdealCustomerProfile} {request.SearchObjective}", @"[^\p{L}\p{N}]+")
            .Where(x => x.Length >= 3 && !stopWords.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .ToArray();
        if (terms.Length == 0)
            return new CrmProspectingProviderResult([], 0, ["French company search requires product or industry keywords."]) { IsComplete = false };

        var records = new List<CrmProspectingRawRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var complete = true;
        var retrievedAt = DateTimeOffset.UtcNow;
        var requestCount = 0;

        foreach (var term in terms)
        {
            var totalPages = 1;
            for (var page = 1; page <= MaxPagesPerTerm; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (requestCount > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);

                var query = new Dictionary<string, string>
                {
                    ["q"] = term,
                    ["page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["per_page"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["minimal"] = "true",
                    ["include"] = "siege"
                };
                var uri = new UriBuilder(Endpoint)
                {
                    Query = string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
                }.Uri;
                using var message = new HttpRequestMessage(HttpMethod.Get, uri);
                message.Headers.Accept.ParseAdd("application/json");
                message.Headers.UserAgent.ParseAdd("KigerCRM-Prospecting/1.0");
                using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                requestCount++;
                if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
                    throw new HttpRequestException("French company search response came from an unapproved endpoint.");
                if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    var retry = response.Headers.RetryAfter?.Date
                        ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddSeconds(5));
                    throw new CrmProspectingProviderRateLimitException("French company search API throttled the request.", (int)response.StatusCode, retry);
                }
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"French company search API returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);

                using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
                var root = document.RootElement;
                if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("French company search response did not contain results.");
                if (root.TryGetProperty("total_pages", out var pages) && pages.TryGetInt32(out var parsedPages))
                    totalPages = Math.Max(1, parsedPages);

                foreach (var item in results.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var siren = Read(item, "siren");
                    var name = Read(item, "nom_complet") ?? Read(item, "nom_raison_sociale");
                    if (string.IsNullOrWhiteSpace(siren) || siren.Length != 9 || !siren.All(char.IsDigit)
                        || string.IsNullOrWhiteSpace(name))
                        continue;
                    if (!seen.Add(siren)) continue;

                    var seat = item.TryGetProperty("siege", out var siege) && siege.ValueKind == JsonValueKind.Object
                        ? siege : default;
                    var siret = Read(seat, "siret");
                    var city = Read(seat, "libelle_commune");
                    var postal = Read(seat, "code_postal");
                    var naf = Read(item, "activite_principale") ?? Read(seat, "activite_principale");
                    var legalState = Read(item, "etat_administratif");
                    var summary = string.Join("; ", new[]
                    {
                        string.IsNullOrWhiteSpace(siret) ? null : $"SIRET: {siret}",
                        string.IsNullOrWhiteSpace(city) ? null : city,
                        string.IsNullOrWhiteSpace(postal) ? null : postal,
                        string.IsNullOrWhiteSpace(naf) ? null : $"NAF/APE: {naf}",
                        string.IsNullOrWhiteSpace(legalState) ? null : $"Administrative status: {legalState}"
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(siren))).ToLowerInvariant();
                    records.Add(new CrmProspectingRawRecord($"FR-SIREN:{siren}", "French Government Business Directory",
                        Limit(name, 500), name, null, null, null, Limit(summary, 1200))
                    {
                        SourceUrl = $"https://annuaire-entreprises.data.gouv.fr/entreprise/{siren}",
                        RetrievedAtUtc = retrievedAt,
                        RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                        EvidenceType = "official_company_registry"
                    });
                }

                if (page >= totalPages) break;
                if (page == MaxPagesPerTerm)
                {
                    complete = false;
                    warnings.Add($"French company search has more pages for keyword '{Limit(term, 60)}'; stopped after {MaxPagesPerTerm} pages.");
                }
            }
        }

        if (records.Count == 0) warnings.Add("French company search returned no usable SIREN company records.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static bool IsFrance(string market)
        => market.Trim().ToUpperInvariant() is "FR" or "FRA" or "FRANCE" or "FRENCH REPUBLIC";

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
            throw new InvalidDataException("French company search response exceeded the 8 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidDataException("French company search response exceeded the 8 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
