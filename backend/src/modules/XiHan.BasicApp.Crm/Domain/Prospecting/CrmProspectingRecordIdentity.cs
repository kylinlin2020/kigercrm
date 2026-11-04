using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Shared identity and payload fingerprint rules for prospecting Candidates and their Evidence.
/// Keep this implementation deterministic and independent of ambient serializer configuration.
/// </summary>
public static class CrmProspectingRecordIdentity
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        WriteIndented = false,
        IncludeFields = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static string NormalizeExternalId(CrmProspectingRawRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var value = string.IsNullOrWhiteSpace(record.ExternalId)
            ? GetCanonicalRecordHash(record)
            : record.ExternalId.Trim();

        return value.Length > 200
            ? "sha256:" + HashUtf8(value)
            : value;
    }

    /// <summary>
    /// Returns the provider's valid SHA-256 when available; otherwise fingerprints the
    /// canonical raw record so content changes are visible to evidence reconciliation.
    /// </summary>
    public static string GetPayloadHash(CrmProspectingRawRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return IsSha256(record.RawPayloadHash)
            ? record.RawPayloadHash!.ToUpperInvariant()
            : GetCanonicalRecordHash(record);
    }

    private static string GetCanonicalRecordHash(CrmProspectingRawRecord record)
    {
        // Retrieval time and an invalid/missing provider hash are transport metadata,
        // not source content. Excluding them keeps retries stable across fetch attempts.
        var canonicalRecord = record with
        {
            ExternalId = string.IsNullOrWhiteSpace(record.ExternalId) ? string.Empty : record.ExternalId.Trim(),
            RetrievedAtUtc = null,
            RawPayloadHash = null
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonicalRecord, CanonicalJsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string HashUtf8(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
