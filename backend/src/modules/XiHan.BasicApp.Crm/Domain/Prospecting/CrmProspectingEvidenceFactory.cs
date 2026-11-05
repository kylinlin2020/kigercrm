using System.Net;
using System.Security.Cryptography;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Builds immutable, unverified evidence rows from provider-supplied candidate fields.</summary>
public static class CrmProspectingEvidenceFactory
{
    public static IReadOnlyList<SysCrmProspectingEvidence> Create(
        long candidateId,
        CrmProspectingRawEnvelope envelope,
        DateTimeOffset fallbackRetrievedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Record);
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));
        if (fallbackRetrievedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Fallback retrieval time must be UTC.", nameof(fallbackRetrievedAtUtc));

        if (string.IsNullOrWhiteSpace(envelope.ProviderCode))
            return Array.Empty<SysCrmProspectingEvidence>();
        var providerCode = envelope.ProviderCode.Trim();
        if (providerCode.Length > 80) return Array.Empty<SysCrmProspectingEvidence>();

        var record = envelope.Record;
        if (!TryGetCanonicalSourceUrl(record.SourceUrl, out var sourceUrl))
            return Array.Empty<SysCrmProspectingEvidence>();
        var retrievedAt = record.RetrievedAtUtc?.ToUniversalTime() ?? fallbackRetrievedAtUtc;
        var evidenceType = record.EvidenceType?.Trim().ToLowerInvariant() switch
        {
            "direct" => "direct",
            "registry" => "registry",
            "provider_asserted" => "provider_asserted",
            "inferred" => "inferred",
            _ => "provider_asserted"
        };
        var payloadHash = CrmProspectingRecordIdentity.GetPayloadHash(record);
        var sourceUrlHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl)));
        // Hash the same trimmed/bounded values that the Candidate normalizer persists.
        // Otherwise an oversized provider field could produce provenance for a value that
        // was silently truncated in the Candidate row.
        var fields = new (string Name, string? Value, bool HashValue)[]
        {
            ("company_name", Bound(string.IsNullOrWhiteSpace(record.CompanyName) ? record.Title : record.CompanyName, 200), true),
            ("title", Bound(record.Title, 300), true),
            ("website", Bound(record.Website, 500), true),
            // Avoid unsalted hashes of low-entropy personal data (e.g. email addresses).
            ("contact_name", Bound(record.ContactName, 200), false),
            ("business_email", Bound(record.BusinessEmail, 254), false),
            ("summary", Bound(record.Summary, 5000), true)
        };

        return fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Value))
            .Select(field => new SysCrmProspectingEvidence
            {
                CandidateId = candidateId,
                ProviderCode = providerCode,
                ProviderRecordId = CrmProspectingRecordIdentity.NormalizeExternalId(record),
                FieldName = field.Name,
                FieldValueHash = field.HashValue
                    ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(field.Value!)))
                    : null,
                SourceUrl = sourceUrl,
                SourceUrlHash = sourceUrlHash,
                EvidenceType = evidenceType,
                VerificationStatus = "unverified",
                EvidenceSummary = $"Provider supplied {field.Name}; requires human verification.",
                PublishedAtUtc = record.PublishedAtUtc?.ToUniversalTime(),
                RetrievedAtUtc = retrievedAt,
                RawPayloadHash = payloadHash
            })
            .ToArray();
    }

    public static bool TryGetCanonicalSourceUrl(string? value, out string canonicalUrl)
    {
        canonicalUrl = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        // DNS treats a trailing root dot as equivalent to the undotted hostname.
        // Normalize it before localhost checks so "localhost." and
        // "service.localhost." cannot bypass the evidence-source guard.
        var host = uri.Host.TrimEnd('.');
        if (host.Length == 0 ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(host, out _))
            return false;

        canonicalUrl = uri.GetLeftPart(UriPartial.Path);
        return canonicalUrl.Length is > 0 and <= 2000;
    }

    private static string? Bound(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

}
