using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Public UK Find a Tender OCDS release search. Results are procurement evidence for buyer
/// organisations; they are not automatically qualified customers or CRM Leads.
/// </summary>
public sealed class CrmFindTenderProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://www.find-tender.service.gov.uk/api/1.0/ocdsReleasePackages");
    private const int MaxPages = 3;
    private const int PageSize = 100;
    private const int MaxResponseBytes = 6 * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public CrmFindTenderProspectingProvider(HttpClient httpClient)
        => _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public string ProviderCode => "uk_find_tender";

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
                ["Find a Tender requires product or industry keywords to avoid collecting unrelated notices."])
            {
                IsComplete = false
            };
        }

        var now = DateTimeOffset.UtcNow;
        var from = (request.CoverageSinceUtc ?? now.AddDays(-30)).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var to = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        string? cursor = null;
        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        var pagesRead = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = $"updatedFrom={Uri.EscapeDataString(from)}&updatedTo={Uri.EscapeDataString(to)}&stages=tender%2Caward&limit={PageSize}";
            if (!string.IsNullOrWhiteSpace(cursor))
                query += $"&cursor={Uri.EscapeDataString(cursor)}";

            var requestUri = new Uri(Endpoint + "?" + query);
            using var message = new HttpRequestMessage(HttpMethod.Get, requestUri);
            message.Headers.Accept.ParseAdd("application/json");
            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
            {
                throw new HttpRequestException("Find a Tender response came from an unapproved endpoint.");
            }

            if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
            {
                var retryAt = response.Headers.RetryAfter?.Date
                    ?? (response.Headers.RetryAfter?.Delta is { } retryDelay
                        ? DateTimeOffset.UtcNow + retryDelay
                        : DateTimeOffset.UtcNow.AddMinutes(5));
                throw new CrmProspectingProviderRateLimitException(
                    "Find a Tender provider throttled the request; retry after the supplied cooldown.",
                    (int)response.StatusCode,
                    retryAt);
            }

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("Find a Tender returned an HTTP error.", null, response.StatusCode);

            var payload = await ReadBoundedAsync(response, MaxResponseBytes, cancellationToken);
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("releases", out var releases) ||
                releases.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Find a Tender response did not contain an OCDS releases array.");
            }

            pagesRead++;
            foreach (var release in releases.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var title = ReadPathString(release, "tender", "title");
                var description = ReadPathString(release, "tender", "description");
                var classification = ReadPathString(release, "tender", "classification", "description");
                var searchable = string.Join(' ', title, description, classification);
                if (!keywords.Any(keyword => searchable.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var buyer = FindBuyer(release);
                var buyerName = buyer is null ? ReadPathString(release, "buyer", "name") : ReadString(buyer.Value, "name");
                if (string.IsNullOrWhiteSpace(buyerName))
                {
                    warnings.Add("A matching Find a Tender release without a buyer name was skipped.");
                    continue;
                }

                var releaseId = ReadString(release, "id") ?? "unknown";
                var ocid = ReadString(release, "ocid") ?? "unknown";
                var noticeUrl = $"https://www.find-tender.service.gov.uk/Notice/{Uri.EscapeDataString(releaseId)}";
                var contact = buyer is null ? default : ReadContact(buyer.Value);
                var summary = BuildSummary(description, classification, ReadPathString(release, "tender", "value", "amount"), ReadPathString(release, "tender", "value", "currency"));

                records.Add(new CrmProspectingRawRecord(
                    $"FINDTENDER:{ocid}:{releaseId}",
                    "Find a Tender",
                    Limit(string.IsNullOrWhiteSpace(title) ? "UK public procurement notice" : title!, 500),
                    Limit(buyerName!, 200),
                    NormalizeHttpsUrl(contact.Website),
                    Limit(contact.ContactName, 200),
                    NormalizeEmail(contact.Email),
                    Limit(summary, 2000))
                {
                    SourceUrl = noticeUrl,
                    PublishedAtUtc = ParseDate(ReadString(release, "date")),
                    RetrievedAtUtc = now,
                    RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(release.GetRawText()))).ToLowerInvariant(),
                    EvidenceType = "official_procurement"
                });
            }

            cursor = ExtractNextCursor(document.RootElement);
            if (string.IsNullOrWhiteSpace(cursor))
                break;
        }

        if (!string.IsNullOrWhiteSpace(cursor))
            warnings.Add($"Find a Tender has additional pages; this adapter stopped after {MaxPages} pages ({pagesRead * PageSize} maximum notices).");

        return new CrmProspectingProviderResult(records, 0, warnings)
        {
            IsComplete = string.IsNullOrWhiteSpace(cursor)
        };
    }

    private static List<string> ExtractKeywords(CrmProspectingProviderRequest request)
        => new[] { request.ProductScope, request.SearchObjective, request.IdealCustomerProfile }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => Regex.Split(value!, @"[^\p{L}\p{N}]+"))
            .Select(value => value.Trim())
            .Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    private static JsonElement? FindBuyer(JsonElement release)
    {
        if (!release.TryGetProperty("parties", out var parties) || parties.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var party in parties.EnumerateArray())
        {
            if (!party.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
                continue;
            if (roles.EnumerateArray().Any(role => string.Equals(role.GetString(), "buyer", StringComparison.OrdinalIgnoreCase)))
                return party;
        }
        return null;
    }

    private static (string? ContactName, string? Email, string? Website) ReadContact(JsonElement party)
    {
        var contact = party.TryGetProperty("contactPoint", out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
        var details = party.TryGetProperty("details", out var detailValue) && detailValue.ValueKind == JsonValueKind.Object
            ? detailValue
            : default;
        return (
            contact.ValueKind == JsonValueKind.Object ? ReadString(contact, "name") : null,
            contact.ValueKind == JsonValueKind.Object ? ReadString(contact, "email") : null,
            details.ValueKind == JsonValueKind.Object ? ReadString(details, "url") : null);
    }

    private static string? ExtractNextCursor(JsonElement root)
    {
        if (!root.TryGetProperty("links", out var links)) return null;
        if (links.ValueKind == JsonValueKind.Object && links.TryGetProperty("next", out var next))
        {
            var nextText = next.GetString();
            if (string.IsNullOrWhiteSpace(nextText)) return null;
            if (!Uri.TryCreate(Endpoint, nextText, out var nextUri) ||
                !string.Equals(nextUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(nextUri.Host, Endpoint.Host, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(nextUri.AbsolutePath, Endpoint.AbsolutePath, StringComparison.Ordinal))
                throw new InvalidDataException("Find a Tender returned a pagination link outside its approved endpoint.");

            var query = nextUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in query)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), "cursor", StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(parts[1]);
            }
            return null;
        }
        return null;
    }

    private static string BuildSummary(string? description, string? classification, string? amount, string? currency)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(description)) parts.Add(description);
        if (!string.IsNullOrWhiteSpace(classification)) parts.Add($"CPV: {classification}");
        if (!string.IsNullOrWhiteSpace(amount)) parts.Add($"Estimated value: {amount} {currency}".Trim());
        return string.Join("; ", parts);
    }

    private static string? ReadPathString(JsonElement element, params string[] path)
    {
        foreach (var part in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out var nextElement))
                return null;
            element = nextElement;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() :
            element.ValueKind == JsonValueKind.Number ? element.GetRawText() : null;
    }

    private static string? ReadString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.ToUniversalTime()
            : null;

    private static string? NormalizeEmail(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 320 &&
           System.Net.Mail.MailAddress.TryCreate(value, out var address) &&
           string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase)
            ? address.Address
            : null;

    private static string? NormalizeHttpsUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
           !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.ToString()
            : null;

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        var length = response.Content.Headers.ContentLength;
        if (length.HasValue && length.Value > maxBytes)
            throw new InvalidDataException("Find a Tender response exceeded the 6 MB limit.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maxBytes)
                throw new InvalidDataException("Find a Tender response exceeded the 6 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string Limit(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= maxLength ? value : value[..maxLength];
}
