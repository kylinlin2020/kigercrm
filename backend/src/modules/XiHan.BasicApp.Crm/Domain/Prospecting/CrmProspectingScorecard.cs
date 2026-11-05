namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Deterministic, explainable prioritization for generator and water-pump prospects.
/// This is a review aid, not a purchase probability or an approval decision.
/// </summary>
public static class CrmProspectingScorecard
{
    public const string Version = "generator-pump-v1";

    public static CrmProspectingScoreResult Evaluate(CrmProspectingScoreInput input, DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (evaluatedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Evaluation time must be UTC.", nameof(evaluatedAtUtc));

        var identity = input.HasVerifiableOrganization ? 15 : 0;
        var procurement = input.HasDirectProcurementSignal ? 30 : 0;
        var productFit = input.HasDirectProductFit ? 20 : 0;
        var marketFit = input.HasTargetMarketFit ? 10 : 0;
        var recency = GetRecencyPoints(input.PublishedAtUtc, evaluatedAtUtc);
        var evidence = input.HasTraceablePrimaryEvidence ? 10 : 0;
        var contact = input.HasPublicBusinessContact ? 5 : 0;
        var total = identity + procurement + productFit + marketFit + recency + evidence + contact;

        var gates = new List<string>();
        if (!input.HasVerifiableOrganization) gates.Add("missing_verifiable_organization");
        if (!input.HasTraceablePrimaryEvidence) gates.Add("missing_traceable_primary_evidence");
        if (!input.HasDirectProcurementSignal) gates.Add("missing_direct_procurement_signal");
        if (input.IsKnownDuplicate) gates.Add("known_duplicate");
        if (input.IsRestrictedOrUnsafeSource) gates.Add("restricted_or_unsafe_source");

        var priority = gates.Count > 0 ? CrmProspectingPriority.Hold : total switch
        {
            >= 80 => CrmProspectingPriority.A,
            >= 60 => CrmProspectingPriority.B,
            >= 40 => CrmProspectingPriority.C,
            _ => CrmProspectingPriority.Hold
        };

        return new CrmProspectingScoreResult(
            Version, total, priority,
            identity, procurement, productFit, marketFit, recency, evidence, contact,
            gates.AsReadOnly(), evaluatedAtUtc);
    }

    private static int GetRecencyPoints(DateTimeOffset? publishedAtUtc, DateTimeOffset evaluatedAtUtc)
    {
        if (publishedAtUtc is null || publishedAtUtc.Value.Offset != TimeSpan.Zero)
            return 0;
        var age = evaluatedAtUtc - publishedAtUtc.Value;
        if (age < TimeSpan.Zero) return 0;
        if (age <= TimeSpan.FromDays(30)) return 10;
        if (age <= TimeSpan.FromDays(90)) return 8;
        if (age <= TimeSpan.FromDays(180)) return 6;
        if (age <= TimeSpan.FromDays(365)) return 3;
        return 0;
    }
}

/// <summary>Only explicit, reviewable facts may contribute points; unknowns remain false/null.</summary>
public sealed record CrmProspectingScoreInput(
    bool HasVerifiableOrganization,
    bool HasDirectProcurementSignal,
    bool HasDirectProductFit,
    bool HasTargetMarketFit,
    DateTimeOffset? PublishedAtUtc,
    bool HasTraceablePrimaryEvidence,
    bool HasPublicBusinessContact,
    bool IsKnownDuplicate = false,
    bool IsRestrictedOrUnsafeSource = false);

public enum CrmProspectingPriority
{
    Hold = 0,
    C = 1,
    B = 2,
    A = 3
}

public sealed record CrmProspectingScoreResult(
    string ScorecardVersion,
    int Total,
    CrmProspectingPriority Priority,
    int IdentityPoints,
    int ProcurementPoints,
    int ProductFitPoints,
    int MarketFitPoints,
    int RecencyPoints,
    int EvidencePoints,
    int ContactPoints,
    IReadOnlyList<string> ReviewGates,
    DateTimeOffset EvaluatedAtUtc);
