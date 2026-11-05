using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Exact-name identity resolver backed by GLEIF's public LEI Records API.
/// A result is returned only when exactly one normalized legal name matches.
/// </summary>
public sealed class CrmGleifIdentityResolver : ICrmProspectingIdentityResolver, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.gleif.org/api/v1/lei-records");
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public CrmGleifIdentityResolver(HttpClient httpClient)
        => _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public void Dispose() => _httpClient.Dispose();

    public async Task<CrmProspectingLegalEntityMatch?> ResolveExactAsync(
        string companyName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        var queryName = companyName.Trim();
        if (queryName.Length > 200)
            queryName = queryName[..200];

        var normalizedQuery = NormalizeName(queryName);
        if (normalizedQuery.Length < 4) return null;

        var query = $"filter%5Bentity.legalName%5D={Uri.EscapeDataString(queryName)}&page%5Bsize%5D=10";
        using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(Endpoint + "?" + query));
        message.Headers.Accept.ParseAdd("application/vnd.api+json");
        message.Headers.UserAgent.ParseAdd("KigerCRM-Prospecting/1.0");

        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.RequestMessage?.RequestUri is not { } finalUri ||
            !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !finalUri.IsDefaultPort ||
            !string.IsNullOrEmpty(finalUri.UserInfo) ||
            !string.Equals(finalUri.Host, Endpoint.Host, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(finalUri.AbsolutePath, Endpoint.AbsolutePath, StringComparison.Ordinal))
        {
            throw new HttpRequestException("GLEIF response came from an unapproved endpoint.");
        }

        if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            var retryAt = response.Headers.RetryAfter?.Date
                ?? (response.Headers.RetryAfter?.Delta is { } retryDelay
                    ? DateTimeOffset.UtcNow + retryDelay
                    : DateTimeOffset.UtcNow.AddMinutes(5));
            throw new CrmProspectingProviderRateLimitException(
                $"GLEIF throttled the identity lookup (HTTP {(int)response.StatusCode}); retry after the supplied cooldown.",
                (int)response.StatusCode, retryAt);
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GLEIF returned HTTP {(int)response.StatusCode}.");

        var payload = await ReadBoundedAsync(response, MaxResponseBytes, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GLEIF response did not contain a JSON:API data array.");

        // If the first page is not the whole result set, uniqueness cannot be established
        // without scanning the complete registry result. Fail closed rather than guessing.
        if (!document.RootElement.TryGetProperty("meta", out var meta) ||
            !meta.TryGetProperty("pagination", out var pagination) ||
            !pagination.TryGetProperty("total", out var totalElement) ||
            !totalElement.TryGetInt32(out var totalMatches) ||
            totalMatches > 10)
            return null;

        var matches = new List<CrmProspectingLegalEntityMatch>();
        foreach (var item in data.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lei = ReadString(item, "id");
            var attributes = GetObject(item, "attributes");
            var entity = GetObject(attributes, "entity");
            var legalName = ReadPathString(entity, "legalName", "name");
            if (string.IsNullOrWhiteSpace(lei) || string.IsNullOrWhiteSpace(legalName) ||
                !string.Equals(NormalizeName(legalName), normalizedQuery, StringComparison.Ordinal))
                continue;

            var registration = GetObject(attributes, "registration");
            var address = GetObject(entity, "legalAddress");
            var sourceUrl = new Uri(Endpoint.ToString().TrimEnd('/') + "/" + Uri.EscapeDataString(lei)).ToString();
            matches.Add(new CrmProspectingLegalEntityMatch(
                lei,
                legalName,
                ReadString(entity, "status"),
                ReadString(registration, "status"),
                ReadString(address, "country"),
                ReadString(entity, "registeredAs"),
                sourceUrl,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                DateTimeOffset.UtcNow));
        }

        // Do not guess when a legal name maps to multiple entities.
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string NormalizeName(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static JsonElement GetObject(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static string? ReadPathString(JsonElement element, params string[] path)
    {
        foreach (var part in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out var next))
                return null;
            element = next;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static string? ReadString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task<byte[]> ReadBoundedAsync(
        HttpResponseMessage response,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var length = response.Content.Headers.ContentLength;
        if (length.HasValue && length.Value > maxBytes)
            throw new InvalidDataException("GLEIF response exceeded the 2 MB limit.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maxBytes)
                throw new InvalidDataException("GLEIF response exceeded the 2 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
