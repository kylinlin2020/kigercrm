using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Hunter Discover finds companies by ICP. It does not reveal contact emails; use a separate
/// approved enrichment workflow for that. Results are discovery evidence, not verified buyers.
/// </summary>
public sealed class CrmHunterDiscoverProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.hunter.io/v2/discover");
    private const int PageSize = 100;
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<string?>> _apiKeyFactory;

    public CrmHunterDiscoverProspectingProvider(HttpClient client, string? apiKey)
        : this(client, _ => Task.FromResult(string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim())) { }

    public CrmHunterDiscoverProspectingProvider(HttpClient client, ISaasConfigurationService configuration)
        : this(client, cancellationToken => configuration.GetStringAsync(
            "CRM.Prospecting.Provider.hunter.ApiKey", cancellationToken: cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(configuration);
    }

    private CrmHunterDiscoverProspectingProvider(HttpClient client, Func<CancellationToken, Task<string?>> apiKeyFactory)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _apiKeyFactory = apiKeyFactory ?? throw new ArgumentNullException(nameof(apiKeyFactory));
    }

    public string ProviderCode => "hunter";
    public void Dispose() => _client.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(
        CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var apiKey = await _apiKeyFactory(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
            return new CrmProspectingProviderResult([], 1,
                ["Hunter Discover requires a tenant API key. Configure it in the provider credentials panel."])
                { IsComplete = false };

        var query = BuildQuery(request);
        if (query.Length == 0)
            return new CrmProspectingProviderResult([], 0,
                ["Hunter Discover requires a specific company ICP or product search objective."])
                { IsComplete = false };

        var uri = new UriBuilder(Endpoint)
        {
            Query = $"api_key={Uri.EscapeDataString(apiKey.Trim())}&limit={PageSize}"
        }.Uri;
        using var message = new HttpRequestMessage(HttpMethod.Post, uri);
        message.Headers.Accept.ParseAdd("application/json");
        message.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new { query }));
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
            throw new HttpRequestException("Hunter Discover response came from an unapproved endpoint.");

        if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            var retryAt = response.Headers.RetryAfter?.Date
                ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddMinutes(5));
            throw new CrmProspectingProviderRateLimitException(
                "Hunter Discover rate limit or temporary service throttle; retry after cooldown.",
                (int)response.StatusCode, retryAt);
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Hunter Discover returned HTTP {(int)response.StatusCode}; verify API access, plan eligibility and usage limits.",
                null, response.StatusCode);

        using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            throw new InvalidDataException("Hunter Discover rejected the request: " + Limit(errors[0].ToString(), 300));
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Hunter Discover response did not contain a data array.");

        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retrievedAt = DateTimeOffset.UtcNow;
        foreach (var item in data.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var domain = Read(item, "domain");
            var company = Read(item, "organization");
            if (!IsValidDomain(domain) || string.IsNullOrWhiteSpace(company))
            {
                warnings.Add("A Hunter Discover row without a valid company domain or organization name was skipped.");
                continue;
            }
            domain = domain!.Trim().TrimEnd('.').ToLowerInvariant();
            if (request.ExcludedDomains.Any(value => IsExcludedDomain(domain, value))) continue;
            if (!seen.Add(domain)) continue;

            var counts = item.TryGetProperty("emails_count", out var emailCounts) ? emailCounts : default;
            var personal = ReadInteger(counts, "personal");
            var generic = ReadInteger(counts, "generic");
            var total = ReadInteger(counts, "total");
            var summary = $"Hunter Discover company match; professional email inventory counts only (personal={personal}, generic={generic}, total={total}); no email addresses were revealed.";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(domain))).ToLowerInvariant();
            records.Add(new CrmProspectingRawRecord(
                $"HUNTER:{hash}", "Hunter Discover", Limit(company, 500), Limit(company, 200),
                $"https://{domain}/", null, null, Limit(summary, 1500))
            {
                SourceUrl = $"https://{domain}/",
                RetrievedAtUtc = retrievedAt,
                RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                EvidenceType = "commercial_company_discovery"
            });
        }

        var isFullPage = data.GetArrayLength() >= PageSize;
        var totalResults = ReadInteger(root.TryGetProperty("meta", out var meta) ? meta : default, "results");
        var complete = !isFullPage && (totalResults <= 0 || totalResults <= data.GetArrayLength());
        if (!complete)
            warnings.Add($"Hunter Discover returned {data.GetArrayLength()} companies (reported total {totalResults}); this adapter intentionally reads one page only. Premium offset pagination is not assumed.");
        if (records.Count == 0)
            warnings.Add("Hunter Discover returned no usable company domains after validation and excluded-domain filtering.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static string BuildQuery(CrmProspectingProviderRequest request)
    {
        var parts = new[]
        {
            request.IdealCustomerProfile,
            string.IsNullOrWhiteSpace(request.ProductScope) ? null : $"companies related to {request.ProductScope}",
            request.TargetMarkets,
            request.SearchObjective
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim());
        return Limit(string.Join("; ", parts), 600);
    }

    private static bool IsValidDomain(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 253
            && Regex.IsMatch(value, @"^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsExcludedDomain(string domain, string excluded)
    {
        var value = excluded.Trim().TrimStart('@', '.').ToLowerInvariant();
        return value.Length > 0 && (domain.Equals(value, StringComparison.OrdinalIgnoreCase)
            || domain.EndsWith("." + value, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Read(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int ReadInteger(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.TryGetInt32(out var number) ? Math.Max(0, number) : 0;

    private static string Limit(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
            throw new InvalidDataException("Hunter Discover response exceeded the 8 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidDataException("Hunter Discover response exceeded the 8 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
