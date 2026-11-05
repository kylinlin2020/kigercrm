using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>Brazil's official PNCP open procurement API; emits tender evidence, never CRM Leads.</summary>
public sealed class CrmBrazilPncpProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://pncp.gov.br/api/consulta/v1/contratacoes/proposta");
    private const int PageSize = 50;
    private const int MaxPages = 5;
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public CrmBrazilPncpProspectingProvider(HttpClient httpClient)
        => _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public string ProviderCode => "brazil_pncp";
    public void Dispose() => _httpClient.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsBrazil(request.TargetMarkets))
            return new CrmProspectingProviderResult([], 0, ["PNCP was skipped because the campaign target market does not include Brazil."]) { IsComplete = true };

        var terms = ExtractKeywords(request);
        if (terms.Count == 0)
            return new CrmProspectingProviderResult([], 0, ["PNCP requires product or industry keywords."]) { IsComplete = false };

        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        var totalPages = 1;
        var rawCount = 0;
        var retrievedAt = DateTimeOffset.UtcNow;
        for (var page = 1; page <= Math.Min(MaxPages, 200); page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = new UriBuilder(Endpoint);
            var query = new Dictionary<string, string>
            {
                ["dataFinal"] = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                ["pagina"] = page.ToString(CultureInfo.InvariantCulture),
                ["tamanhoPagina"] = PageSize.ToString(CultureInfo.InvariantCulture)
            };
            uri.Query = string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
            using var message = new HttpRequestMessage(HttpMethod.Get, uri.Uri);
            message.Headers.Accept.ParseAdd("application/json");
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
                throw new HttpRequestException("PNCP response came from an unapproved endpoint.");
            if (response.StatusCode == HttpStatusCode.NoContent) break;
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                var retryAt = response.Headers.RetryAfter?.Date
                    ?? (response.Headers.RetryAfter?.Delta is { } delta ? DateTimeOffset.UtcNow + delta : DateTimeOffset.UtcNow.AddMinutes(5));
                throw new CrmProspectingProviderRateLimitException("PNCP throttled the request; retry after the supplied cooldown.", (int)response.StatusCode, retryAt);
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("PNCP returned an HTTP error.", null, response.StatusCode);

            using var document = JsonDocument.Parse(await ReadBoundedAsync(response, MaxResponseBytes, cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("PNCP response did not contain a valid data array.");
            rawCount += data.GetArrayLength();
            totalPages = ReadInt(root, "totalPaginas", totalPages);
            foreach (var item in data.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = ReadString(item, "numeroControlePNCP");
                var description = ReadString(item, "objetoCompra");
                var organization = ReadPathString(item, "orgaoEntidade", "razaoSocial");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(organization))
                {
                    warnings.Add("A PNCP notice without a stable control number, purchase object or contracting organization was skipped.");
                    continue;
                }
                var searchable = $"{description} {organization}";
                if (!terms.Any(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;

                var unit = ReadPathString(item, "unidadeOrgao", "nomeUnidade");
                var city = ReadPathString(item, "unidadeOrgao", "municipioNome");
                var state = ReadPathString(item, "unidadeOrgao", "ufSigla");
                var modality = ReadString(item, "modalidadeNome");
                var deadline = ReadString(item, "dataEncerramentoProposta");
                var amount = ReadDecimal(item, "valorTotalEstimado");
                var summary = string.Join("; ", new[]
                {
                    modality,
                    amount is null ? null : $"Estimated amount BRL {amount.Value.ToString("0.##", CultureInfo.InvariantCulture)}",
                    string.IsNullOrWhiteSpace(deadline) ? null : $"Proposal deadline {deadline}",
                    string.IsNullOrWhiteSpace(city) ? null : $"Location {city}/{state}"
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                records.Add(new CrmProspectingRawRecord(
                    $"PNCP:{id}", "Brazil PNCP", Limit(description, 500), Limit(organization, 200),
                    null, Limit(unit, 200), null, Limit(summary, 2000))
                {
                    SourceUrl = $"https://pncp.gov.br/app/editais?q={Uri.EscapeDataString(id)}",
                    PublishedAtUtc = ParseDate(ReadString(item, "dataPublicacaoPncp")),
                    RetrievedAtUtc = retrievedAt,
                    RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                    EvidenceType = "official_procurement"
                });
            }
            if (page >= totalPages || data.GetArrayLength() == 0) break;
        }

        var complete = totalPages <= MaxPages;
        if (!complete)
            warnings.Add($"PNCP reported {totalPages} pages; this bounded run read only {MaxPages}. Coverage is incomplete.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static bool IsBrazil(string value)
        => Regex.IsMatch(value ?? string.Empty, @"\b(brazil|brasil|br)\b|巴西", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<string> ExtractKeywords(CrmProspectingProviderRequest request)
        => new[] { request.ProductScope, request.SearchObjective, request.IdealCustomerProfile }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => Regex.Split(value!, @"[^\p{L}\p{N}]+"))
            .Select(value => value.Trim()).Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > maxBytes)
            throw new InvalidDataException("PNCP response exceeded the 8 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maxBytes) throw new InvalidDataException("PNCP response exceeded the 8 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? ReadPathString(JsonElement element, string parent, string property)
        => element.TryGetProperty(parent, out var node) && node.ValueKind == JsonValueKind.Object ? ReadString(node, property) : null;
    private static int ReadInt(JsonElement element, string property, int fallback)
        => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) && result >= 0 ? result : fallback;
    private static decimal? ReadDecimal(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result) && result >= 0 ? result : null;
    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToUniversalTime() : null;
    private static string Limit(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
