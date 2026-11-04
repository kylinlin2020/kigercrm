using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Builds a stable, credential-free SHA-256 fingerprint for a provider request.
/// RunId is intentionally excluded so equivalent requests can be compared across Runs.
/// Callers must never put secrets into ProviderOptionsJson.
/// </summary>
public static class CrmProspectingRequestFingerprint
{
    public static string Create(string providerCode, CrmProspectingProviderRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        ArgumentNullException.ThrowIfNull(request);

        var canonical = new
        {
            ProviderCode = NormalizeText(providerCode).ToLowerInvariant(),
            SearchObjective = NormalizeText(request.SearchObjective),
            ProductScope = NormalizeText(request.ProductScope),
            TargetMarkets = NormalizeText(request.TargetMarkets),
            IdealCustomerProfile = NormalizeText(request.IdealCustomerProfile),
            ProviderOptions = CanonicalizeJson(request.ProviderOptionsJson),
            ExcludedDomains = (request.ExcludedDomains ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeDomain)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string NormalizeText(string? value)
        => string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeDomain(string value)
    {
        var normalized = NormalizeText(value).Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized.StartsWith("https://", StringComparison.Ordinal))
            normalized = normalized[8..];
        else if (normalized.StartsWith("http://", StringComparison.Ordinal))
            normalized = normalized[7..];
        if (normalized.StartsWith("www.", StringComparison.Ordinal))
            normalized = normalized[4..];
        return normalized.Trim('/');
    }

    private static string CanonicalizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "{}";
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(document.RootElement, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException("Unsupported JSON token in provider options.");
        }
    }
}
