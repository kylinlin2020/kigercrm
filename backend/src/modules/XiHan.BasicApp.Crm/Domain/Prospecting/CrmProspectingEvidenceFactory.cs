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
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));
        if (fallbackRetrievedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Fallback retrieval time must be UTC.", nameof(fallbackRetrievedAtUtc));

        if (!TryGetCanonicalSourceUrl(envelope.Record.SourceUrl, out var sourceUrl))
            return Array.Empty<SysCrmProspectingEvidence>();

        var providerCode = envelope.ProviderCode.Trim();
        if (providerCode.Length is 0 or > 80) return Array.Empty<SysCrmProspectingEvidence>();

        var record = envelope.Record;
        var retrievedAt = record.RetrievedAtUtc?.ToUniversalTime() ?? fallbackRetrievedAtUtc;
        var evidenceType = record.EvidenceType?.Trim().ToLowerInvariant() switch
        {
            "direct" => "direct",
            "registry" => "registry",
            "provider_asserted" => "provider_asserted",
            "inferred" => "inferred",
            _ => "provider_asserted"
        };
        var payloadHash = IsSha256(record.RawPayloadHash) ? record.RawPayloadHash!.ToUpperInvariant() : string.Empty;
        var sourceUrlHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl)));
        var fields = new (string Name, string? Value, bool HashValue)[]
        {
            ("company_name", record.CompanyName, true),
            ("title", record.Title, true),
            ("website", record.Website, true),
            // Avoid unsalted hashes of low-entropy personal data (e.g. email addresses).
            ("contact_name", record.ContactName, false),
            ("business_email", record.BusinessEmail, false),
            ("summary", record.Summary, true)
        };

        return fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Value))
            .Select(field => new SysCrmProspectingEvidence
            {
                CandidateId = candidateId,
                ProviderCode = providerCode,
                ProviderRecordId = string.IsNullOrWhiteSpace(record.ExternalId)
                    ? string.Empty
                    : record.ExternalId[..Math.Min(record.ExternalId.Length, 240)],
                FieldName = field.Name,
                FieldValueHash = field.HashValue
                    ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(field.Value!.Trim())))
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

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
