using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Official US FMCSA QCMobile carrier lookup. A WebKey is required and is sent only as a query
/// parameter to the fixed official host; diagnostics must never include the request URI.
/// </summary>
public sealed class CrmFmcsaQcmobileProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private static readonly Uri ApprovedEndpoint = new("https://mobile.fmcsa.dot.gov/qc/services/carriers/name/UPS");
    private const string Host = "mobile.fmcsa.dot.gov";
    private const string PathPrefix = "/qc/services/carriers/name/";
    private const int MaxResponseBytes = 4 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly Func<CancellationToken, Task<string?>> _webKeyFactory;

    public CrmFmcsaQcmobileProspectingProvider(HttpClient httpClient, string? webKey)
        : this(httpClient, _ => Task.FromResult(string.IsNullOrWhiteSpace(webKey) ? null : webKey.Trim())) { }

    public CrmFmcsaQcmobileProspectingProvider(HttpClient httpClient, ISaasConfigurationService configuration)
        : this(httpClient, cancellationToken => configuration.GetStringAsync(
            "CRM.Prospecting.Provider.fmcsa_qcmobile.WebKey", cancellationToken: cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(configuration);
    }

    private CrmFmcsaQcmobileProspectingProvider(
        HttpClient httpClient,
        Func<CancellationToken, Task<string?>> webKeyFactory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _webKeyFactory = webKeyFactory ?? throw new ArgumentNullException(nameof(webKeyFactory));
    }

    public string ProviderCode => "fmcsa_qcmobile";
    public void Dispose() => _httpClient.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(
        CrmProspectingProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsUnitedStates(request.TargetMarkets))
            return new CrmProspectingProviderResult([], 0,
                ["FMCSA QCMobile was skipped because the campaign target market does not include the United States."])
                { IsComplete = true };

        var terms = ExtractKeywords(request);
        if (terms.Count == 0)
            return new CrmProspectingProviderResult([], 0,
                ["FMCSA QCMobile requires a carrier, logistics or company keyword."])
                { IsComplete = false };

        var webKey = await _webKeyFactory(cancellationToken);
        if (string.IsNullOrWhiteSpace(webKey))
            return new CrmProspectingProviderResult([], 1,
                ["FMCSA QCMobile requires a tenant WebKey. Configure it in the provider credentials panel."])
                { IsComplete = false };
        if (webKey.Length > 512 || webKey.Any(char.IsControl))
            throw new InvalidOperationException("FMCSA WebKey configuration is invalid.");

        var query = string.Join(" ", terms);
        var requestUri = new UriBuilder(new Uri(new Uri("https://" + Host), PathPrefix + Uri.EscapeDataString(query)));
        requestUri.Query = "webKey=" + Uri.EscapeDataString(webKey.Trim());
        using var message = new HttpRequestMessage(HttpMethod.Get, requestUri.Uri);
        message.Headers.Accept.ParseAdd("application/json");

        using var response = await _httpClient.SendAsync(
            message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, ApprovedEndpoint))
            throw new HttpRequestException("FMCSA response came from an unapproved endpoint.");

        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            var retryAt = response.Headers.RetryAfter?.Date
                ?? (response.Headers.RetryAfter?.Delta is { } delta
                    ? DateTimeOffset.UtcNow + delta
                    : DateTimeOffset.UtcNow.AddMinutes(5));
            throw new CrmProspectingProviderRateLimitException(
                "FMCSA QCMobile temporarily throttled the request; retry after the cooldown.",
                (int)response.StatusCode,
                retryAt);
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("FMCSA QCMobile returned an HTTP error.", null, response.StatusCode);

        using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
        if (!document.RootElement.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("FMCSA response did not contain a valid carrier list.");

        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        var retrievedAt = DateTimeOffset.UtcNow;
        foreach (var item in content.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.TryGetProperty("carrier", out var carrier) || carrier.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("An FMCSA result without a carrier object was skipped.");
                continue;
            }

            var dotNumber = ReadScalar(carrier, "dotNumber");
            var legalName = ReadString(carrier, "legalName");
            if (string.IsNullOrWhiteSpace(dotNumber) || string.IsNullOrWhiteSpace(legalName))
            {
                warnings.Add("An FMCSA result without USDOT identity or legal name was skipped.");
                continue;
            }

            var dbaName = ReadString(carrier, "dbaName");
            var allowedToOperate = ReadString(carrier, "allowedToOperate");
            var statusCode = ReadString(carrier, "statusCode");
            var country = ReadString(carrier, "phyCountry") ?? "United States";
            var city = ReadString(carrier, "phyCity");
            var state = ReadString(carrier, "phyState");
            var street = ReadString(carrier, "phyStreet");
            var phone = ReadString(carrier, "telephone");
            var email = ReadString(carrier, "emailAddress");
            var contact = string.IsNullOrWhiteSpace(email) ? phone : email;
            var description = string.Join("; ", new[]
            {
                string.IsNullOrWhiteSpace(dbaName) ? null : "DBA: " + dbaName,
                string.IsNullOrWhiteSpace(allowedToOperate) ? null : "Allowed to operate: " + allowedToOperate,
                string.IsNullOrWhiteSpace(statusCode) ? null : "Status: " + statusCode,
                string.Join(" ", new[] { street, city, state }.Where(value => !string.IsNullOrWhiteSpace(value)))
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            var sourceUrl = "https://safer.fmcsa.dot.gov/query.asp?searchtype=ANY&query_type=queryCarrierSnapshot&query_param=USDOT&query_string="
                + Uri.EscapeDataString(dotNumber);
            var rawHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant();
            records.Add(new CrmProspectingRawRecord(
                "USDOT:" + Limit(dotNumber, 64),
                "US FMCSA QCMobile",
                "US motor carrier",
                Limit(legalName, 200),
                null,
                Limit(dbaName, 200),
                Limit(email, 254),
                Limit(description, 5000))
            {
                SourceUrl = sourceUrl,
                RetrievedAtUtc = retrievedAt,
                RawPayloadHash = rawHash,
                EvidenceType = "registry"
            });

            if (records.Count >= 100) break;
        }

        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = true };
    }

    private static bool IsUnitedStates(string value)
        => Regex.IsMatch(value ?? string.Empty,
            @"\b(united states|u\.s\.?a?\.?|usa|us)\b|美国",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<string> ExtractKeywords(CrmProspectingProviderRequest request)
        => new[] { request.ProductScope, request.SearchObjective, request.IdealCustomerProfile }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => Regex.Split(value!, @"[^\p{L}\p{N}]+"))
            .Select(value => value.Trim())
            .Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    private async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
            throw new InvalidDataException("FMCSA response exceeded the 4 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidDataException("FMCSA response exceeded the 4 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadScalar(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static string Limit(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= maxLength ? value : value[..maxLength];
}
