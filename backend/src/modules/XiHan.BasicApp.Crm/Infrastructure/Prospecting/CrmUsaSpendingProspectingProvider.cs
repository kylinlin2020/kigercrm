using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Official, keyless USAspending award search. It is deliberately restricted to the fixed
/// public API endpoint and emits procurement evidence, never CRM Leads.
/// </summary>
public sealed class CrmUsaSpendingProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.usaspending.gov/api/v2/search/spending_by_award/");
    private static readonly string[] Fields =
    [
        "Award ID", "Recipient Name", "Award Amount", "Description",
        "Start Date", "End Date", "Awarding Agency"
    ];

    private readonly HttpClient _httpClient;

    public CrmUsaSpendingProspectingProvider(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string ProviderCode => "usaspending_awards";

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
                ["USAspending requires at least one meaningful product or industry keyword."]);
        }

        var today = DateTime.UtcNow.Date;
        var body = JsonSerializer.Serialize(new
        {
            filters = new
            {
                time_period = new[]
                {
                    new
                    {
                        start_date = today.AddMonths(-24).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        end_date = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    }
                },
                award_type_codes = new[] { "A", "B", "C", "D" },
                keywords
            },
            fields = Fields,
            page = 1,
            limit = 20,
            subawards = false
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

        // Redirects are disabled by the typed-client registration. Fail closed if a custom
        // HttpClient handler nevertheless follows one or changes the destination.
        if (response.RequestMessage?.RequestUri is not { } finalUri ||
            !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(finalUri.Host, Endpoint.Host, StringComparison.OrdinalIgnoreCase) ||
            finalUri.AbsolutePath != Endpoint.AbsolutePath)
        {
            throw new HttpRequestException("USAspending response came from an unapproved endpoint.");
        }

        if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            var retryAt = response.Headers.RetryAfter?.Date
                ?? (response.Headers.RetryAfter?.Delta is { } retryDelay
                    ? DateTimeOffset.UtcNow + retryDelay
                    : DateTimeOffset.UtcNow.AddMinutes(5));
            throw new CrmProspectingProviderRateLimitException(
                "USAspending provider throttled the request; retry after the supplied cooldown.",
                (int)response.StatusCode,
                retryAt);
        }

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("USAspending returned an HTTP error.", null, response.StatusCode);

        var payload = await ReadBoundedAsync(response, 6 * 1024 * 1024, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("USAspending response did not contain a valid results array.");
        }

        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        foreach (var item in results.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var awardId = ReadString(item, "Award ID");
            var recipient = ReadString(item, "Recipient Name");
            if (string.IsNullOrWhiteSpace(awardId) || string.IsNullOrWhiteSpace(recipient))
            {
                warnings.Add("A USAspending result without an award ID or recipient name was skipped.");
                continue;
            }

            var description = ReadString(item, "Description");
            var agency = ReadString(item, "Awarding Agency");
            var amount = ReadDecimal(item, "Award Amount");
            var startDate = ReadString(item, "Start Date");
            var endDate = ReadString(item, "End Date");
            var summaryParts = new[]
            {
                description,
                string.IsNullOrWhiteSpace(agency) ? null : $"Awarding agency: {agency}",
                amount is null ? null : $"Award amount: {amount.Value.ToString("0.##", CultureInfo.InvariantCulture)} USD",
                string.IsNullOrWhiteSpace(startDate) ? null : $"Start: {startDate}",
                string.IsNullOrWhiteSpace(endDate) ? null : $"End: {endDate}"
            }.Where(value => !string.IsNullOrWhiteSpace(value));
            var summary = string.Join("; ", summaryParts);
            var awardReference = ReadString(item, "generated_internal_id");
            if (string.IsNullOrWhiteSpace(awardReference)) awardReference = awardId;

            records.Add(new CrmProspectingRawRecord(
                $"USASPENDING:{awardId}",
                "USAspending",
                string.IsNullOrWhiteSpace(description) ? "US federal award record" : Limit(description, 500),
                Limit(recipient, 200),
                null,
                null,
                null,
                Limit(summary, 2000))
            {
                SourceUrl = $"https://www.usaspending.gov/award/{Uri.EscapeDataString(awardReference)}/",
                RetrievedAtUtc = DateTimeOffset.UtcNow,
                RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                EvidenceType = "official_procurement"
            });
        }

        var isComplete = true;
        if (document.RootElement.TryGetProperty("page_metadata", out var metadata) &&
            metadata.TryGetProperty("hasNext", out var hasNext) &&
            hasNext.ValueKind == JsonValueKind.True)
        {
            isComplete = false;
            warnings.Add("USAspending has additional pages; this adapter currently reads the first page only.");
        }

        return new CrmProspectingProviderResult(records, 0, warnings)
        {
            IsComplete = isComplete
        };
    }

    private static List<string> ExtractKeywords(CrmProspectingProviderRequest request)
    {
        var values = new[] { request.ProductScope, request.SearchObjective, request.IdealCustomerProfile };
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => Regex.Split(value!, @"[^\p{L}\p{N}]+"))
            .Select(value => value.Trim())
            .Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpResponseMessage response,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maxBytes)
            throw new InvalidDataException("USAspending response exceeded the 6 MB limit.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maxBytes)
                throw new InvalidDataException("USAspending response exceeded the 6 MB limit.");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static decimal? ReadDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return null;
    }

    private static string Limit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
