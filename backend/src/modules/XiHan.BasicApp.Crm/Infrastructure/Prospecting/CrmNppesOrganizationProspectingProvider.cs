using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// CMS NPPES organization-only discovery. NPI Type 2 is an identity/registry signal,
/// not proof of licensure, credentialing, purchasing intent, or a verified buyer.
/// </summary>
public sealed class CrmNppesOrganizationProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://npiregistry.cms.hhs.gov/api/");
    private const int PageSize = 200;
    private const int MaxTerms = 2;
    private const int MaxPagesPerTerm = 2;
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private readonly HttpClient _client;

    public CrmNppesOrganizationProspectingProvider(HttpClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    public string ProviderCode => "nppes";
    public void Dispose() => _client.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(
        CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsUnitedStatesTarget(request.TargetMarkets))
            return new CrmProspectingProviderResult([], 0,
                ["NPPES organization search is limited to United States target campaigns."]) { IsComplete = true };
        if (!IsHealthcareCampaign(request))
            return new CrmProspectingProviderResult([], 0,
                ["NPPES is restricted to explicitly healthcare-related campaigns."]) { IsComplete = true };

        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "find","search","discover","prospect","prospecting","lead","leads","target","market",
            "company","companies","business","businesses","healthcare","health","medical","provider",
            "providers","organization","organizations","equipment","device","devices","pump","pumps","generator","generators","supplier","suppliers","distributor","distributors","the","and","for","with","from","into","of","in","to"
        };
        var terms = Regex.Split($"{request.ProductScope} {request.IdealCustomerProfile} {request.SearchObjective}",
                @"[^\p{L}\p{N}]+")
            .Where(term => term.Length >= 3 && !stop.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .ToArray();
        if (terms.Length == 0)
            return new CrmProspectingProviderResult([], 0,
                ["NPPES requires a specific healthcare taxonomy or organization keyword."]) { IsComplete = false };

        var records = new List<CrmProspectingRawRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var complete = true;
        var retrievedAt = DateTimeOffset.UtcNow;

        foreach (var term in terms)
        {
            for (var page = 0; page < MaxPagesPerTerm; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parameters = new Dictionary<string, string>
                {
                    ["version"] = "2.1",
                    ["enumeration_type"] = "NPI-2",
                    ["taxonomy_description"] = term,
                    ["limit"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["skip"] = (page * PageSize).ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
                var uri = new UriBuilder(Endpoint)
                {
                    Query = string.Join("&", parameters.Select(pair =>
                        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
                }.Uri;
                using var message = new HttpRequestMessage(HttpMethod.Get, uri);
                message.Headers.Accept.ParseAdd("application/json");
                message.Headers.UserAgent.ParseAdd("KigerCRM-Prospecting/1.0 (https://kin-power.com)");
                using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
                    throw new HttpRequestException("NPPES response came from an unapproved endpoint.");
                if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    var retryAt = response.Headers.RetryAfter?.Date
                        ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddMinutes(15));
                    throw new CrmProspectingProviderRateLimitException(
                        "NPPES throttled the organization search; reduce request frequency and retry after cooldown.",
                        (int)response.StatusCode, retryAt);
                }
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"NPPES returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);

                using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
                var root = document.RootElement;
                if (root.TryGetProperty("Errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                {
                    var description = errors[0].TryGetProperty("description", out var d) ? d.GetString() : null;
                    throw new InvalidDataException("NPPES rejected the search criteria: " + Limit(description, 250));
                }
                if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("NPPES response did not contain a results array.");

                foreach (var item in results.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!string.Equals(Read(item, "enumeration_type"), "NPI-2", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var npi = Read(item, "number");
                    if (string.IsNullOrWhiteSpace(npi) || !Regex.IsMatch(npi, @"^\d{10}$", RegexOptions.CultureInvariant)
                        || !seen.Add(npi))
                        continue;
                    var basic = item.TryGetProperty("basic", out var b) ? b : default;
                    var name = Read(basic, "organization_name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        warnings.Add("An NPPES Type 2 record without organization_name was skipped.");
                        continue;
                    }
                    var status = Read(basic, "status");
                    var taxonomy = ReadPrimaryTaxonomy(item);
                    var summary = string.Join("; ", new[]
                    {
                        "NPI Type 2 organization record",
                        string.IsNullOrWhiteSpace(status) ? null : $"NPI status: {status}",
                        string.IsNullOrWhiteSpace(taxonomy) ? null : $"Primary taxonomy: {taxonomy}",
                        "NPI issuance does not establish licensure or credentialing"
                    }.Where(value => value is not null));
                    records.Add(new CrmProspectingRawRecord(
                        $"NPPES:{npi}", "CMS NPPES NPI Registry", Limit(name, 500), Limit(name, 200),
                        null, null, null, Limit(summary, 1500))
                    {
                        SourceUrl = $"https://npiregistry.cms.hhs.gov/provider-view/{npi}",
                        RetrievedAtUtc = retrievedAt,
                        PublishedAtUtc = ParseDate(Read(basic, "last_updated")),
                        RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                        EvidenceType = "us_healthcare_organization_registry"
                    });
                }

                if (results.GetArrayLength() < PageSize) break;
                if (page == MaxPagesPerTerm - 1)
                {
                    complete = false;
                    warnings.Add($"NPPES keyword '{Limit(term, 60)}' reached the bounded {MaxPagesPerTerm}-page window; additional organizations may exist.");
                }
            }
        }

        if (records.Count == 0)
            warnings.Add("NPPES returned no usable Type 2 organizations for these healthcare keywords; this does not establish that no organizations exist.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static bool IsUnitedStatesTarget(string markets)
        => Regex.Split(markets ?? string.Empty, @"[,;，；|]+").Select(value => value.Trim())
            .Any(value => value.Equals("United States", StringComparison.OrdinalIgnoreCase)
                || value.Equals("USA", StringComparison.OrdinalIgnoreCase)
                || value.Equals("US", StringComparison.OrdinalIgnoreCase)
                || value.Equals("United States of America", StringComparison.OrdinalIgnoreCase));

    private static bool IsHealthcareCampaign(CrmProspectingProviderRequest request)
        => Regex.IsMatch($"{request.ProductScope} {request.IdealCustomerProfile} {request.SearchObjective}",
            @"\b(healthcare|health care|medical|clinical|hospital|clinic|diagnostic|physician|nursing|dental|pharmacy|laboratory|ambulance|surgical)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? ReadPrimaryTaxonomy(JsonElement item)
    {
        if (!item.TryGetProperty("taxonomies", out var values) || values.ValueKind != JsonValueKind.Array) return null;
        foreach (var taxonomy in values.EnumerateArray())
            if (taxonomy.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True)
                return Read(taxonomy, "desc") ?? Read(taxonomy, "code");
        return null;
    }

    private static string? Read(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToUniversalTime() : null;

    private static string Limit(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= max ? value : value[..max];

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
            throw new InvalidDataException("NPPES response exceeded the 8 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes) throw new InvalidDataException("NPPES response exceeded the 8 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
