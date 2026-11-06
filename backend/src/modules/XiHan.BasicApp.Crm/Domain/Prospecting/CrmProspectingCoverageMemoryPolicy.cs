using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Canonical search scope. Moving incremental time bounds are deliberately not part of the scope identity.</summary>
public sealed record CrmProspectingCoverageScope(
    string ProviderCode,
    IReadOnlyList<string> Products,
    IReadOnlyList<string> Markets,
    IReadOnlyList<string> CustomerTypes,
    IReadOnlyList<string> Industries,
    IReadOnlyList<string> ExcludedTerms,
    IReadOnlyList<string> ExcludedDomains,
    string QueryPlannerVersion);

/// <summary>Exact time window used by one scan; stored separately from the stable scope fingerprint.</summary>
public sealed record CrmProspectingCoverageWindow(
    DateTimeOffset? SinceUtc,
    DateTimeOffset? UntilUtc);

/// <summary>
/// Deterministic coverage-memory identity and watermark policy. Persistence and transaction boundaries
/// belong to the repository/worker; this class cannot advance a database watermark itself.
/// </summary>
public static class CrmProspectingCoverageMemoryPolicy
{
    public static string CreateScopeFingerprint(CrmProspectingCoverageScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.ProviderCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.QueryPlannerVersion);
        var canonical = new
        {
            provider = Normalize(scope.ProviderCode, 80).ToLowerInvariant(),
            products = NormalizeList(scope.Products, 30, 200),
            markets = NormalizeList(scope.Markets, 30, 120),
            customerTypes = NormalizeList(scope.CustomerTypes, 20, 160),
            industries = NormalizeList(scope.Industries, 30, 200),
            excludedTerms = NormalizeList(scope.ExcludedTerms, 100, 200),
            excludedDomains = NormalizeList(scope.ExcludedDomains, 500, 253)
                .Select(value => value.TrimStart('.').TrimEnd('.').ToLowerInvariant())
                .OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            planner = Normalize(scope.QueryPlannerVersion, 80)
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
    }

    /// <summary>Creates the tenant/provider/external-record observation key used for cross-Run linking.</summary>
    public static string CreateObservationKey(string providerCode, CrmProspectingRawRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        ArgumentNullException.ThrowIfNull(record);
        var provider = Normalize(providerCode, 80).ToLowerInvariant();
        var identity = CrmProspectingRecordIdentity.NormalizeExternalId(record);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}\n{identity}")));
    }

    /// <summary>
    /// A complete-scan watermark may advance only after a Completed extraction with every page complete,
    /// zero record failures, no truncation, and no cancellation/review condition.
    /// </summary>
    public static bool CanAdvanceCompleteWatermark(
        CrmProspectingExtractionStatus status,
        bool everyPageComplete,
        int failureCount,
        bool limitReached,
        bool cancelled,
        bool needsReview)
        => status == CrmProspectingExtractionStatus.Completed
            && everyPageComplete
            && failureCount == 0
            && !limitReached
            && !cancelled
            && !needsReview;

    private static string[] NormalizeList(
        IReadOnlyList<string> values,
        int maximumCount,
        int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > maximumCount) throw new ArgumentOutOfRangeException(nameof(values));
        return values.Select(value => Normalize(value, maximumLength))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Normalize(string value, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(char.IsControl))
            throw new ArgumentException("Coverage scope values cannot contain control characters.");
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(nameof(value));
        return normalized;
    }
}
