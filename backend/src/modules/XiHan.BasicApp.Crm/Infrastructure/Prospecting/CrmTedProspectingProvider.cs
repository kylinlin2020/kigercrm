using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Public EU TED Search API adapter. Published notices are evidence about buyers and opportunities,
/// not automatically qualified CRM Leads.
/// </summary>
public sealed class CrmTedProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.ted.europa.eu/v3/notices/search");
    private const int MaxPages = 2;
    private const int PageSize = 100;
    private const int MaxResponseBytes = 6 * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public CrmTedProspectingProvider(HttpClient httpClient)
        => _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public string ProviderCode => "eu_ted";

    public void Dispose() => _httpClient.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(
        CrmProspectingProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var keywords = ExtractKeywords(request);
        if (keywords.Count == 0)
        {
            return new CrmProspectingProviderResult(
                [],
                0,
                ["TED requires product or industry keywords to avoid collecting unrelated notices."]);
        }

        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-30).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var to = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var terms = string.Join(" OR ", keywords.Select(keyword => $"\\"{keyword}\\""));
        var expertQuery = $"FT=({terms}) AND PD=({from} <> {to})";
        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();

        for (var page = 1; page <= MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = JsonSerializer.Serialize(new
            {
                query = expertQuery,
                fields = new[]
                {
                    "publication-number", "notice-title", "buyer-name",
                    "buyer-country", "publication-date", "deadline-date", "form-type"
                },
                page,
                limit = PageSize,
                scope = "ACTIVE",
                checkQuerySyntax = false,
                paginationMode = "PAGE_NUMBER"
            });

            using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            message.Headers.Accept.ParseAdd("application/json");
            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.RequestMessage?.RequestUri is not { } finalUri ||
                !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(finalUri.Host, Endpoint.Host, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(finalUri.AbsolutePath, Endpoint.AbsolutePath, StringComparison.Ordinal))
                throw new HttpRequestException("TED response came from an unapproved endpoint.");

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                var retryAt = response.Headers.RetryAfter?.Date
                    ?? (response.Headers.RetryAfter?.Delta is { } retryDelay
                        ? DateTimeOffset.UtcNow + retryDelay
                        : DateTimeOffset.UtcNow.AddMinutes(5));
                throw new CrmProspectingProviderRateLimitException(
                    $"TED throttled the request (HTTP {(int)response.StatusCode}); retry after the supplied cooldown.",
                    (int)response.StatusCode, retryAt);
            }

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"TED Search API returned HTTP {(int)response.StatusCode}.");

            var payload = await ReadBoundedAsync(response, MaxResponseBytes, cancellationToken);
            using var document = JsonDocument.Parse(payload);
            var results = TryGetArray(document.RootElement, "results") ?? TryGetArray(document.RootElement, "notices");
            if (results is null)
                throw new InvalidDataException("TED Search API response did not contain a results/notices array.");

            foreach (var item in results.Value.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var publicationNumber = ReadText(item, "publication-number");
                var buyerName = ReadText(item, "buyer-name");
                var title = ReadText(item, "notice-title");
                if (string.IsNullOrWhiteSpace(publicationNumber) || string.IsNullOrWhiteSpace(buyerName))
                {
                    warnings.Add("A TED notice without a publication number or buyer name was skipped.");
                    continue;
                }

                var country = ReadText(item, "buyer-country");
                var published = ParseDate(ReadText(item, "publication-date"));
                var deadline = ReadText(item, "deadline-date");
                var formType = ReadText(item, "form-type");
                var summary = string.Join("; ", new[]
                {
                    string.IsNullOrWhiteSpace(country) ? null : $"Buyer country: {country}",
                    string.IsNullOrWhiteSpace(formType) ? null : $"Notice type: {formType}",
                    string.IsNullOrWhiteSpace(deadline) ? null : $"Submission deadline: {deadline}"
                }.Where(value => !string.IsNullOrWhiteSpace(value)));

                records.Add(new CrmProspectingRawRecord(
                    $"TED:{publicationNumber}",
                    "TED",
                    Limit(string.IsNullOrWhiteSpace(title) ? "EU public procurement notice" : title!, 500),
                    Limit(buyerName!, 200),
                    null,
                    null,
                    null,
                    Limit(summary, 2000))
                {
                    SourceUrl = $"https://ted.europa.eu/en/notice/-/detail/{Uri.EscapeDataString(publicationNumber)}",
                    PublishedAtUtc = published,
                    RetrievedAtUtc = now,
                    RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                    EvidenceType = "official_procurement"
                });
            }

            var total = ReadInt(document.RootElement, "total");
            if (results.Value.GetArrayLength() < PageSize || (total.HasValue && page * PageSize >= total.Value))
                break;
            if (page == MaxPages)
                warnings.Add($"TED has additional matching notices; this adapter stopped after {MaxPages} pages ({MaxPages * PageSize} maximum notices).");
        }

        return new CrmProspectingProviderResult(records, 0, warnings);
    }

    private static List<string> ExtractKeywords(CrmProspectingProviderRequest request)
    {
        // Product scope is the strongest intent signal. Search objective is a fallback;
        // generic ICP words such as "public authority" create noisy full-text searches.
        var source = string.IsNullOrWhiteSpace(request.ProductScope)
            ? request.SearchObjective
            : request.ProductScope;
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "procurement", "purchase", "purchasing", "supply", "supplies", "supplier",
            "suppliers", "buyer", "buyers", "public", "private", "authority", "authorities",
            "government", "company", "companies", "market", "markets", "target", "customer",
            "customers", "business", "businesses", "equipment", "solutions"
        };
        return Regex.Split(source ?? string.Empty, @"[^\p{L}\p{N}]+")
            .Select(value => value.Trim())
            .Where(value => value.Length >= 3 && !stopWords.Contains(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static JsonElement? TryGetArray(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.Array ? value : null;

    private static string? ReadText(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return null;
        return FlattenText(value);
    }

    private static string? FlattenText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind == JsonValueKind.Number) return value.GetRawText();
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var text = FlattenText(item);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "value", "text", "content", "name" })
            {
                if (value.TryGetProperty(key, out var nested))
                {
                    var text = FlattenText(nested);
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
        }
        return null;
    }

    private static int? ReadInt(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.TryGetInt32(out var number) ? number : null;

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var compact))
            return compact.ToUniversalTime();
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToUniversalTime() : null;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        var length = response.Content.Headers.ContentLength;
        if (length.HasValue && length.Value > maxBytes)
            throw new InvalidDataException("TED response exceeded the 6 MB limit.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maxBytes)
                throw new InvalidDataException("TED response exceeded the 6 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string Limit(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= maxLength ? value : value[..maxLength];
}