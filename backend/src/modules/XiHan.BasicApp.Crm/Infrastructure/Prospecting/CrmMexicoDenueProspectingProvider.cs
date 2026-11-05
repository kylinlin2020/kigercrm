using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Mexico INEGI DENUE official business directory. Its token is embedded in the URL path,
/// so request URIs must never be written to diagnostics and response redirects are rejected.
/// </summary>
public sealed class CrmMexicoDenueProspectingProvider : ICrmProspectingProvider, IDisposable
{
    private const string Host = "www.inegi.org.mx";
    private const string PathPrefix = "/app/api/denue/v1/consulta/Buscar/";
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private const int PageSize = 20;
    private const int MaxPosition = 1000;
    private readonly HttpClient _httpClient;
    private readonly Func<CancellationToken, Task<string?>> _tokenFactory;

    public CrmMexicoDenueProspectingProvider(HttpClient httpClient, string? token)
        : this(httpClient, _ => Task.FromResult(string.IsNullOrWhiteSpace(token) ? null : token.Trim())) { }

    public CrmMexicoDenueProspectingProvider(HttpClient httpClient, ISaasConfigurationService configuration)
        : this(httpClient, cancellationToken => configuration.GetStringAsync(
            "CRM.Prospecting.Provider.mexico_denue.ApiKey", cancellationToken: cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(configuration);
    }

    private CrmMexicoDenueProspectingProvider(HttpClient httpClient, Func<CancellationToken, Task<string?>> tokenFactory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenFactory = tokenFactory ?? throw new ArgumentNullException(nameof(tokenFactory));
    }

    public string ProviderCode => "mexico_denue";
    public void Dispose() => _httpClient.Dispose();

    public async Task<CrmProspectingProviderResult> SearchAsync(
        CrmProspectingProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsMexico(request.TargetMarkets))
            return new CrmProspectingProviderResult([], 0, ["DENUE was skipped because the campaign target market does not include Mexico."]) { IsComplete = true };

        var terms = ExtractKeywords(request);
        if (terms.Count == 0)
            return new CrmProspectingProviderResult([], 0, ["DENUE requires a product, industry or company keyword."]) { IsComplete = false };

        var token = await _tokenFactory(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return new CrmProspectingProviderResult([], 1, ["DENUE requires a tenant API token. Configure it in the provider credentials panel."]) { IsComplete = false };
        if (token.Length > 1024 || token.Any(char.IsControl) || token.Contains('/'))
            throw new InvalidOperationException("DENUE token configuration is invalid.");

        var records = new List<CrmProspectingRawRecord>();
        var warnings = new List<string>();
        var retrievedAt = DateTimeOffset.UtcNow;
        var complete = true;
        var totalRead = 0;
        for (var start = 1; start <= MaxPosition; start += PageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = Math.Min(start + PageSize - 1, MaxPosition);
            var term = Uri.EscapeDataString(string.Join(" ", terms));
            var safeToken = Uri.EscapeDataString(token);
            var uri = new Uri($"https://{Host}{PathPrefix}{term}/00/{start}/{end}/{safeToken}");
            var approvedEndpoint = uri;
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Accept.ParseAdd("application/json");
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!CrmProspectingEndpointGuard.IsApprovedResponseUri(response.RequestMessage?.RequestUri, approvedEndpoint))
                throw new HttpRequestException("DENUE response came from an unapproved endpoint.");
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                var retryAt = response.Headers.RetryAfter?.Date
                    ?? (response.Headers.RetryAfter?.Delta is { } delta ? DateTimeOffset.UtcNow + delta : DateTimeOffset.UtcNow.AddMinutes(5));
                throw new CrmProspectingProviderRateLimitException("DENUE temporarily throttled the request; retry after the cooldown.", (int)response.StatusCode, retryAt);
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("DENUE returned an HTTP error.", null, response.StatusCode);

            using var document = JsonDocument.Parse(await ReadBoundedAsync(response, cancellationToken));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("DENUE response did not contain a valid business array.");
            var pageCount = document.RootElement.GetArrayLength();
            totalRead += pageCount;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = ReadString(item, "Id");
                var name = ReadString(item, "Nombre");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                {
                    warnings.Add("A DENUE result without a stable ID or business name was skipped.");
                    continue;
                }
                var legalName = ReadString(item, "Razon_social");
                var activity = ReadString(item, "Clase_actividad");
                var website = NormalizeHttpUrl(ReadString(item, "Sitio_internet"));
                var email = ReadString(item, "Correo_e");
                var phone = ReadString(item, "Telefono");
                var address = string.Join(" ", new[] {
                    ReadString(item, "Tipo_vialidad"), ReadString(item, "Calle"), ReadString(item, "Num_Exterior"),
                    ReadString(item, "Colonia"), ReadString(item, "CP"), ReadString(item, "Municipio"), ReadString(item, "Entidad")
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                var summary = string.Join("; ", new[] {
                    activity, string.IsNullOrWhiteSpace(ReadString(item, "Estrato")) ? null : "Size: " + ReadString(item, "Estrato"),
                    string.IsNullOrWhiteSpace(address) ? null : "Address: " + address,
                    string.IsNullOrWhiteSpace(phone) ? null : "Phone: " + phone
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                var sourceUrl = "https://www.inegi.org.mx/app/mapa/denue/default.aspx?id=" + Uri.EscapeDataString(id);
                records.Add(new CrmProspectingRawRecord("DENUE:" + Limit(id, 64), "Mexico INEGI DENUE",
                    Limit(activity ?? "Mexican business establishment", 500), Limit(legalName ?? name, 200),
                    website, null, Limit(email, 254), Limit(summary, 5000))
                {
                    SourceUrl = sourceUrl,
                    RetrievedAtUtc = retrievedAt,
                    RawPayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant(),
                    EvidenceType = "official_business_registry"
                });
            }

            if (pageCount < PageSize || start + PageSize > MaxPosition) break;
            if (records.Count >= 100) { complete = false; warnings.Add("DENUE result cap reached; provider coverage is incomplete."); break; }
        }
        if (totalRead >= MaxPosition) { complete = false; warnings.Add("DENUE reached its 1,000-record API position limit; provider coverage is incomplete."); }
        return new CrmProspectingProviderResult(records, 0, warnings) { IsComplete = complete };
    }

    private static bool IsMexico(string value)
        => Regex.IsMatch(value ?? string.Empty, @"\b(mexico|méxico)\b|墨西哥", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<string> ExtractKeywords(CrmProspectingProviderRequest request)
        => new[] { request.ProductScope, request.SearchObjective, request.IdealCustomerProfile }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => Regex.Split(value!, @"[^\p{L}\p{N}]+"))
            .Select(value => value.Trim()).Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();

    private static string? NormalizeHttpUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.HostNameType == UriHostNameType.Unknown) return null;
        return uri.ToString().Length <= 2000 ? uri.ToString() : null;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxResponseBytes)
            throw new InvalidDataException("DENUE response exceeded the 8 MB limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes) throw new InvalidDataException("DENUE response exceeded the 8 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string? ReadString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string Limit(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
