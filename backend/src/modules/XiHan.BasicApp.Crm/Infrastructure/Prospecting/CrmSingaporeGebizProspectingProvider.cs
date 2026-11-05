using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>Singapore GeBIZ award dataset mirrored on data.gov.sg; award evidence is not an open tender.</summary>
public sealed class CrmSingaporeGebizProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://data.gov.sg/api/action/datastore_search");
    private const string ResourceId = "d_acde1106003906a75c3fa052592f2fcb";
    private const int PageSize = 20;
    private const int MaxPages = 5;
    private const int MaxResponseBytes = 6 * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public CrmSingaporeGebizProspectingProvider(HttpClient httpClient)
        => _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public string ProviderCode => "singapore_gebiz";
    public void Dispose() => _httpClient.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Regex.IsMatch(request.TargetMarkets ?? string.Empty, @"\b(singapore|sg)\b|新加坡", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new CrmProspectingProviderResult([], 0, ["GeBIZ was skipped because the campaign target market does not include Singapore."]) { IsComplete = true };

        var terms = ExtractKeywords(request);
        if (terms.Count == 0)
            return new CrmProspectingProviderResult([], 0, ["GeBIZ requires product or industry keywords."]) { IsComplete = false };

        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        var total = 0;
        var rawCount = 0;
        var retrievedAt = DateTimeOffset.UtcNow;
        for (var page = 0; page < MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = page * PageSize;
            var uri = new UriBuilder(Endpoint);
            var query = new Dictionary<string, string>
            {
                ["resource_id"] = ResourceId,
                ["limit"] = PageSize.ToString(CultureInfo.InvariantCulture),
                ["offset"] = offset.ToString(CultureInfo.InvariantCulture),
                ["q"] = string.Join(' ', terms)
            };
            uri.Query = string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
            using var message = new HttpRequestMessage(HttpMethod.Get, uri.Uri);
            message.Headers.Accept.ParseAdd("application/json");
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
                throw new HttpRequestException("GeBIZ response came from an unapproved endpoint.");
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                var retryAt = response.Headers.RetryAfter?.Date
                    ?? (response.Headers.RetryAfter?.Delta is { } delta ? DateTimeOffset.UtcNow + delta : DateTimeOffset.UtcNow.AddMinutes(5));
                throw new CrmProspectingProviderRateLimitException("GeBIZ throttled the request; retry after the supplied cooldown.", (int)response.StatusCode, retryAt);
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("GeBIZ returned an HTTP error.", null, response.StatusCode);

            using var document = JsonDocument.Parse(await ReadBoundedAsync(response, MaxResponseBytes, cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("result", out var result)
                || !result.TryGetProperty("records", out var rows) || rows.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GeBIZ response did not contain a valid data.gov.sg records array.");
            total = ReadInt(result, "total");
            rawCount += rows.GetArrayLength();
            foreach (var item in rows.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tenderNo = ReadString(item, "tender_no");
                var description = ReadString(item, "tender_description");
                var agency = ReadString(item, "agency");
                if (string.IsNullOrWhiteSpace(tenderNo) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(agency))
                {
                    warnings.Add("A GeBIZ award record without tender number, description or agency was skipped.");
                    continue;
                }
                if (!terms.Any(term => $"{description} {agency}".Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
                var supplier = ReadString(item, "supplier_name");
                var amount = ReadStringOrNumber(item, "awarded_amt");
                var awardDate = ReadString(item, "award_date");
                var status = ReadString(item, "tender_detail_status");
                var summary = string.Join("; ", new[]
                {
                    $"Buyer agency: {agency}",
                    string.IsNullOrWhiteSpace(supplier) ? null : $"Awarded supplier: {supplier}",
                    string.IsNullOrWhiteSpace(amount) ? null : $"Award amount SGD {amount}",
                    string.IsNullOrWhiteSpace(awardDate) ? null : $"Award date {awardDate}",
                    string.IsNullOrWhiteSpace(status) ? null : $"Status {status}"
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                var rowId = ReadStringOrNumber(item, "_id") ?? tenderNo;
                records.Add(new CrmProspectingRawRecord(
                    $"GEBIZ:{tenderNo}:{rowId}", "Singapore GeBIZ", Limit(description, 500),
                    Limit(agency, 200), null, null, null, Limit(summary, 2000))
                {
                    SourceUrl = $"https://www.gebiz.gov.sg/ptn/opportunity/BOListing.xhtml?origin=search&tenderNo={Uri.EscapeDataString(tenderNo)}",
                    PublishedAtUtc = ParseDate(awardDate),
                    RetrievedAtUtc = retrievedAt,
                    RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                    EvidenceType = "official_procurement_award"
                });
            }
            if (offset + rows.GetArrayLength() >= total || rows.GetArrayLength() == 0) break;
        }

        var complete = rawCount >= total || total <= PageSize * MaxPages;
        var warningsOut = warnings;
        warningsOut.Add("GeBIZ dataset represents awarded tenders; it is historical buyer/supplier evidence, not proof that a tender is still open.");
        if (!complete) warningsOut.Add($"GeBIZ reported {total} records; this bounded run read at most {PageSize * MaxPages}. Coverage is incomplete.");
        return new CrmProspectingProviderResult(records, 0, warningsOut) { IsComplete = complete };
    }

    private static List<string> ExtractKeywords(CrmProspectingProviderRequest request)
        => new[] { request.ProductScope, request.SearchObjective, request.IdealCustomerProfile }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => Regex.Split(value!, @"[^\p{L}\p{N}]+"))
            .Select(value => value.Trim()).Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > maxBytes)
            throw new InvalidDataException("GeBIZ response exceeded the 6 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maxBytes) throw new InvalidDataException("GeBIZ response exceeded the 6 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? ReadStringOrNumber(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }
    private static int ReadInt(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) && result >= 0 ? result : 0;
    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToUniversalTime() : null;
    private static string Limit(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
