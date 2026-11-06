using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Hard resource limits for one Deep Mining mission.</summary>
public sealed record CrmProspectingDeepMiningLimits(
    int MaximumDepth,
    int MaximumRootCandidates,
    int MaximumQueries,
    int MaximumWebsiteProbes);

/// <summary>Result of evaluating whether a related-company mining task may be queued.</summary>
public sealed record CrmProspectingDeepMiningDecision(bool Allowed, string ReasonCode);

/// <summary>
/// Deterministic, bounded Deep Mining policy. It decides whether work may be planned; it does not
/// crawl websites, call providers, create Candidates, or change verification status.
/// </summary>
public static class CrmProspectingDeepMiningPolicy
{
    private static readonly string[] RelationshipTerms =
    [
        "subsidiary", "parent company", "distributor", "dealer", "reseller",
        "buyer", "supplier", "contract award", "project contractor",
        "authorized partner", "regional representative"
    ];

    public static CrmProspectingDeepMiningLimits GetLimits(CrmProspectingSuperSearchDepth depth)
        => depth switch
        {
            CrmProspectingSuperSearchDepth.Balanced => new(1, 3, 3, 2),
            CrmProspectingSuperSearchDepth.Deep => new(2, 10, 7, 5),
            CrmProspectingSuperSearchDepth.Extreme => new(3, 20, 15, 10),
            _ => throw new ArgumentOutOfRangeException(nameof(depth))
        };

    public static CrmProspectingDeepMiningDecision Evaluate(
        CrmProspectingSuperSearchDepth depth,
        int rootCandidateCount,
        int taskDepth,
        int queriesUsed,
        int websiteProbesUsed,
        bool cancelled = false,
        bool deadlineReached = false,
        bool costLimitReached = false,
        bool duplicateSaturationReached = false)
    {
        var limits = GetLimits(depth);
        if (cancelled) return new(false, "cancelled");
        if (deadlineReached) return new(false, "deadline_reached");
        if (costLimitReached) return new(false, "cost_limit_reached");
        if (duplicateSaturationReached) return new(false, "duplicate_saturation");
        if (rootCandidateCount < 0 || taskDepth < 0 || queriesUsed < 0 || websiteProbesUsed < 0)
            throw new ArgumentOutOfRangeException(nameof(rootCandidateCount), "Mining counters cannot be negative.");
        if (rootCandidateCount > limits.MaximumRootCandidates) return new(false, "root_candidate_limit");
        if (taskDepth >= limits.MaximumDepth) return new(false, "maximum_depth");
        if (queriesUsed >= limits.MaximumQueries) return new(false, "query_limit");
        if (websiteProbesUsed >= limits.MaximumWebsiteProbes) return new(false, "website_probe_limit");
        if (rootCandidateCount == 0) return new(false, "no_root_candidates");
        return new(true, "within_limits");
    }

    /// <summary>Creates a stable task identity so retries cannot enqueue duplicate work for the same candidate path.</summary>
    public static string CreateTaskId(string missionId, string rootCandidateId, string candidateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootCandidateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        return "pdmt_" + Fingerprint(new { missionId, rootCandidateId, candidateId })[..32].ToLowerInvariant();
    }

    /// <summary>
    /// Builds a bounded relationship query around one existing candidate. The returned text is a
    /// hypothesis for discovery only; it is not a verified relationship or purchase-intent assertion.
    /// </summary>
    public static string BuildRelationshipQuery(
        string companyName,
        string? businessDescription,
        string? country,
        IReadOnlyList<string>? excludedTerms = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        var company = Normalize(companyName, 200);
        var business = Normalize(businessDescription ?? string.Empty, 200);
        var market = Normalize(country ?? string.Empty, 120);
        var exclusions = (excludedTerms ?? [])
            .Select(term => Normalize(term, 120))
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToArray();

        var query = string.Join(" ", new[] { company, business, market }
            .Where(value => value.Length > 0)
            .Concat(RelationshipTerms));
        if (exclusions.Length > 0)
            query += " -" + string.Join(" -", exclusions);
        return query.Length <= 1200 ? query : query[..1200];
    }

    private static string Normalize(string value, int maxLength)
    {
        if (value.Any(char.IsControl))
            throw new ArgumentException("Mining query values cannot contain control characters.");
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string Fingerprint<T>(T value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
