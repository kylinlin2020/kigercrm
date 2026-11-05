using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>SEC EDGAR company tickers is an identity directory, not a buyer-intent or contact database.</summary>
public sealed class CrmSecEdgarProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://www.sec.gov/files/company_tickers.json");
    private const int MaxResponseBytes = 12 * 1024 * 1024;
    private const int MaxRecords = 100;
    private static readonly SemaphoreSlim RequestScheduleGate = new(1, 1);
    private static DateTimeOffset _nextRequestAtUtc = DateTimeOffset.MinValue;
    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<string?>> _userAgentFactory;

    public CrmSecEdgarProspectingProvider(HttpClient client, string? userAgent)
        : this(client, _ => Task.FromResult(string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim())) { }

    public CrmSecEdgarProspectingProvider(HttpClient client, ISaasConfigurationService configuration)
        : this(client, cancellationToken => configuration.GetStringAsync(
            "CRM.Prospecting.Provider.sec_edgar.UserAgent", cancellationToken: cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(configuration);
    }

    private CrmSecEdgarProspectingProvider(HttpClient client, Func<CancellationToken, Task<string?>> userAgentFactory)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _userAgentFactory = userAgentFactory ?? throw new ArgumentNullException(nameof(userAgentFactory));
    }

    public string ProviderCode => "sec_edgar";
    public void Dispose() => _client.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(
        CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsUnitedStatesTarget(request.TargetMarkets))
            return new CrmProspectingProviderResult([], 0,
                ["SEC EDGAR was skipped because the target market is not the United States."])
                { IsComplete = true };

        var userAgent = await _userAgentFactory(cancellationToken);
        if (!IsValidUserAgent(userAgent))
            return new CrmProspectingProviderResult([], 1,
                ["SEC EDGAR requires a descriptive User-Agent containing a reachable contact email; enter a value such as 'KigerCRM admin@example.com'."])
                { IsComplete = false };

        var terms = BuildTerms(request);
        if (terms.Length == 0)
            return new CrmProspectingProviderResult([], 0,
                ["SEC EDGAR requires a company name, ticker, product or industry keyword."])
                { IsComplete = false };

        await WaitForFairAccessSlotAsync(cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        message.Headers.Accept.ParseAdd("application/json");
        message.Headers.TryAddWithoutValidation("User-Agent", userAgent!.Trim());
        using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, Endpoint))
            throw new HttpRequestException("SEC EDGAR response came from an unapproved endpoint.");

        if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            var retryAt = response.Headers.RetryAfter?.Date
                ?? (response.Headers.RetryAfter?.Delta is { } delay ? DateTimeOffset.UtcNow + delay : DateTimeOffset.UtcNow.AddMinutes(5));
            throw new CrmProspectingProviderRateLimitException(
                "SEC EDGAR Fair Access throttled the request; retry after cooldown.",
                (int)response.StatusCode, retryAt);
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"SEC EDGAR returned HTTP {(int)response.StatusCode}; verify Fair Access User-Agent and service availability.",
                null, response.StatusCode);

        using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() > 25_000)
            throw new InvalidDataException("SEC EDGAR company ticker directory returned an unexpected structure.");

        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retrievedAt = DateTimeOffset.UtcNow;
        foreach (var property in root.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = property.Value;
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("cik_str", out var cikElement)
                || !item.TryGetProperty("ticker", out var tickerElement)
                || !item.TryGetProperty("title", out var titleElement))
                continue;

            var cik = cikElement.ValueKind == JsonValueKind.Number && cikElement.TryGetInt64(out var number)
                ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : cikElement.ValueKind == JsonValueKind.String ? cikElement.GetString() : null;
            var ticker = tickerElement.ValueKind == JsonValueKind.String ? tickerElement.GetString() : null;
            var title = titleElement.ValueKind == JsonValueKind.String ? titleElement.GetString() : null;
            if (!long.TryParse(cik, out var parsedCik) || parsedCik <= 0
                || string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(title))
            {
                warnings.Add("SEC EDGAR skipped a company directory row with invalid CIK, ticker or title.");
                continue;
            }

            var normalizedTitle = Normalize(title);
            var normalizedTicker = ticker.Trim().ToUpperInvariant();
            var matched = terms.Any(term => normalizedTitle.Contains(term, StringComparison.Ordinal)
                || normalizedTicker.Equals(term, StringComparison.OrdinalIgnoreCase));
            if (!matched) continue;

            var stableCik = parsedCik.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
            if (!seen.Add(stableCik)) continue;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant();
            var sourceUrl = $"https://www.sec.gov/edgar/browse/?CIK={stableCik}&owner=exclude";
            records.Add(new CrmProspectingRawRecord(
                $"SEC:CIK:{stableCik}", "SEC EDGAR", Limit(title, 500), Limit(title, 200),
                null, null, null, $"SEC ticker/CIK association record; CIK {stableCik}; ticker {normalizedTicker}. This file does not cover every SEC filer or private company.")
            {
                SourceUrl = sourceUrl,
                RetrievedAtUtc = retrievedAt,
                RawPayloadHash = hash,
                EvidenceType = "us_sec_company_directory"
            });
            if (records.Count >= MaxRecords) break;
        }

        var truncated = records.Count >= MaxRecords;
        if (truncated)
            warnings.Add($"SEC EDGAR matched at least {MaxRecords} companies; the adapter stopped at its bounded result limit.");
        if (records.Count == 0)
            warnings.Add("SEC EDGAR returned no matching company-ticker records; this file is a ticker/CIK association list, not a complete register of all US companies or private businesses.");
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = !truncated };
    }

    private static async Task WaitForFairAccessSlotAsync(CancellationToken cancellationToken)
    {
        await RequestScheduleGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_nextRequestAtUtc > now)
                await Task.Delay(_nextRequestAtUtc - now, cancellationToken);
            _nextRequestAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(150);
        }
        finally
        {
            RequestScheduleGate.Release();
        }
    }

    private static bool IsUnitedStatesTarget(string value)
        => Regex.Split(value ?? "", @"[,;，；|]+").Any(part =>
            part.Trim().ToUpperInvariant() is "US" or "USA" or "UNITED STATES" or "UNITED STATES OF AMERICA");

    private static bool IsValidUserAgent(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 200
            && Regex.IsMatch(value.Trim(), @"^[^\s@]+\s+[^\s@]+@[^\s@]+\.[^\s@]+$",
                RegexOptions.CultureInvariant);

    private static string[] BuildTerms(CrmProspectingProviderRequest request)
    {
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "find","search","discover","prospect","prospecting","lead","leads","target","market",
            "company","companies","business","businesses","supplier","suppliers","buyer","buyers",
            "distributor","distributors","importer","importers","product","products","the","and",
            "for","with","from","into","of","in","to","a","an","united","states","usa"
        };
        return Regex.Split($"{request.ProductScope} {request.IdealCustomerProfile} {request.SearchObjective}",
                @"[^\p{L}\p{N}]+")
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => value.Length >= 2 && !stop.Contains(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToArray();
    }

    private static string Normalize(string value)
        => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    private static string Limit(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
            throw new InvalidDataException("SEC EDGAR response exceeded the 12 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidDataException("SEC EDGAR response exceeded the 12 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
