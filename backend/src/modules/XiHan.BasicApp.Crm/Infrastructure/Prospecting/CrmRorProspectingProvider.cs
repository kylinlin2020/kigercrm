using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>ROR is an identifier registry for research organizations, not a general business directory.</summary>
public sealed class CrmRorProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.ror.org/v2/organizations");
    private readonly HttpClient _client;
    public CrmRorProspectingProvider(HttpClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));
    public string ProviderCode => "ror";
    public void Dispose() => _client.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "find","search","discover","prospect","lead","leads","company","companies","business","businesses","the","and","for","with","from","of","in","to","a","an" };
        var terms = Regex.Split(request.ProductScope + " " + request.IdealCustomerProfile + " " + request.SearchObjective, @"[^\p{L}\p{N}]+")
            .Where(x => x.Length >= 3 && !stop.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        if (terms.Length == 0) return new CrmProspectingProviderResult([], 0, ["ROR requires product or institution keywords."]) { IsComplete = false };

        var records = new List<CrmProspectingRawRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var complete = true;
        foreach (var term in terms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = new Uri(Endpoint + "?query=" + Uri.EscapeDataString(term) + "&page=1");
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Accept.ParseAdd("application/json");
            message.Headers.UserAgent.ParseAdd("KigerCRM-Prospecting/1.0");
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
                throw new HttpRequestException("ROR response came from an unapproved endpoint.");
            if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
            {
                var retry = response.Headers.RetryAfter?.Date ?? (response.Headers.RetryAfter?.Delta is { } d ? DateTimeOffset.UtcNow + d : DateTimeOffset.UtcNow.AddMinutes(5));
                throw new CrmProspectingProviderRateLimitException("ROR throttled the organization search.", (int)response.StatusCode, retry);
            }
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"ROR returned HTTP {(int)response.StatusCode}.");
            using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("ROR response did not contain items.");
            var metadata = root.TryGetProperty("metadata", out var m) ? m : default;
            var total = metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("number_of_results", out var n) && n.TryGetInt32(out var parsed) ? parsed : 0;
            if (total > 20) { complete = false; warnings.Add($"ROR has {total} matches for '{term}'; only the first 20 were read."); }

            foreach (var item in items.EnumerateArray())
            {
                var id = Read(item, "id");
                var name = ReadName(item);
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !seen.Add(id)) continue;
                if (!Uri.TryCreate(id, UriKind.Absolute, out var recordUri) || recordUri.Scheme != "https" || !recordUri.Host.Equals("ror.org", StringComparison.OrdinalIgnoreCase))
                { warnings.Add("Skipped a ROR record with an invalid identifier."); continue; }
                var status = Read(item, "status");
                var types = ReadArray(item, "types");
                var country = ReadCountry(item);
                var website = ReadLink(item);
                var summary = string.Join("; ", new[] {
                    string.IsNullOrWhiteSpace(status) ? null : "Status: " + status,
                    types.Count == 0 ? null : "Organization type: " + string.Join(", ", types),
                    string.IsNullOrWhiteSpace(country) ? null : "Country: " + country
                }.Where(x => x is not null));
                records.Add(new CrmProspectingRawRecord("ROR:" + recordUri.AbsolutePath.Trim('/'), "Research Organization Registry (ROR)", name, name,
                    SafeHttps(website) ? website : null, null, null, summary) {
                    SourceUrl = recordUri.ToString(), RetrievedAtUtc = DateTimeOffset.UtcNow,
                    RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                    EvidenceType = "research_organization_registry"
                });
            }
        }
        if (records.Count == 0) warnings.Add("No matching research-organization records were found; ROR is not a general business directory.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static string? ReadName(JsonElement item)
    {
        if (!item.TryGetProperty("names", out var names) || names.ValueKind != JsonValueKind.Array) return null;
        foreach (var entry in names.EnumerateArray())
        {
            var value = Read(entry, "value");
            var types = ReadArray(entry, "types");
            if (!string.IsNullOrWhiteSpace(value) && types.Contains("ror_display", StringComparer.OrdinalIgnoreCase)) return value;
        }
        foreach (var entry in names.EnumerateArray()) { var value = Read(entry, "value"); if (!string.IsNullOrWhiteSpace(value)) return value; }
        return null;
    }
    private static string? ReadCountry(JsonElement item)
    {
        if (!item.TryGetProperty("locations", out var a) || a.ValueKind != JsonValueKind.Array) return null;
        foreach (var x in a.EnumerateArray()) if (x.TryGetProperty("geonames_details", out var g) && Read(g, "country_name") is { } s) return s;
        return null;
    }
    private static string? ReadLink(JsonElement item)
    {
        if (!item.TryGetProperty("links", out var a) || a.ValueKind != JsonValueKind.Array) return null;
        foreach (var x in a.EnumerateArray()) if (Read(x, "value") is { } s) return s;
        return null;
    }
    private static bool SafeHttps(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme == "https";
    private static string? Read(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static List<string> ReadArray(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : [];
    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        const int max = 4 * 1024 * 1024;
        if (response.Content.Headers.ContentLength is long n && n > max) throw new InvalidDataException("ROR response exceeded 4 MB.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true) { var read = await input.ReadAsync(buffer.AsMemory(), ct); if (read == 0) break; if (output.Length + read > max) throw new InvalidDataException("ROR response exceeded 4 MB."); output.Write(buffer, 0, read); }
        return output.ToArray();
    }
}
