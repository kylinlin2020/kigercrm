using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

public sealed record CrmExamCertificateVerdict(bool Valid, string Reason);

/// <summary>
/// Deterministic certificate rules: one certificate per passed attempt, a stable number/hash
/// derived from immutable data, and verification that checks revocation and expiry separately.
/// </summary>
public static class CrmExamCertificatePolicy
{
    public static readonly TimeSpan Validity = TimeSpan.FromDays(365);

    public static (bool Allowed, string Reason) CanIssue(
        CrmExamAttemptStatus attemptStatus,
        bool alreadyIssued)
    {
        if (attemptStatus != CrmExamAttemptStatus.Passed) return (false, "attempt_not_passed");
        if (alreadyIssued) return (false, "already_issued");
        return (true, "ok");
    }

    public static string BuildNumber(long tenantId, long attemptId, DateTimeOffset issuedAtUtc)
        => $"EX-{attemptId.ToString(CultureInfo.InvariantCulture)}-{issuedAtUtc.UtcDateTime:yyyyMMdd}";

    public static string BuildHash(
        string certificateNo,
        long attemptId,
        long holderUserId,
        int score,
        int totalScore,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset validUntilUtc)
    {
        // Normalise to whole seconds: PostgreSQL timestamptz stores microseconds while
        // DateTimeOffset.UtcNow carries 100ns ticks, so a full-precision "O" string would differ
        // after a round-trip and break verification. Second-level precision is stable everywhere.
        var payload = string.Join('|',
            certificateNo,
            attemptId.ToString(CultureInfo.InvariantCulture),
            holderUserId.ToString(CultureInfo.InvariantCulture),
            score.ToString(CultureInfo.InvariantCulture),
            totalScore.ToString(CultureInfo.InvariantCulture),
            CanonicalUtc(issuedAtUtc),
            CanonicalUtc(validUntilUtc));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string CanonicalUtc(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static CrmExamCertificateVerdict Verify(
        SysCrmExamCertificate certificate,
        string presentedHash,
        DateTimeOffset nowUtc)
    {
        var expected = BuildHash(
            certificate.CertificateNo,
            certificate.AttemptId,
            certificate.HolderUserId,
            certificate.Score,
            certificate.TotalScore,
            certificate.IssuedAtUtc,
            certificate.ValidUntilUtc);
        if (!string.Equals(expected, presentedHash?.Trim(), StringComparison.OrdinalIgnoreCase))
            return new CrmExamCertificateVerdict(false, "hash_mismatch");
        if (certificate.Status == CrmExamCertificateStatus.Revoked)
            return new CrmExamCertificateVerdict(false, "revoked");
        if (nowUtc > certificate.ValidUntilUtc)
            return new CrmExamCertificateVerdict(false, "expired");
        return new CrmExamCertificateVerdict(true, "valid");
    }
}
