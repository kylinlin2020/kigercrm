using System.Text.Json;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Maps deterministic score output to immutable persistence without changing the score.</summary>
public static class CrmProspectingScoreSnapshotFactory
{
    public static SysCrmProspectingScore Create(long candidateId, CrmProspectingScoreResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));
        if (string.IsNullOrWhiteSpace(result.ScorecardVersion) || result.ScorecardVersion.Length > 80)
            throw new ArgumentException("Scorecard version must contain 1-80 characters.", nameof(result));
        if (result.Total is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(result), "Score total must be between 0 and 100.");
        if (result.EvaluatedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Score evaluation time must be UTC.", nameof(result));

        if (!Enum.IsDefined(result.Priority))
            throw new ArgumentOutOfRangeException(nameof(result), "Priority value is not defined.");

        if (result.IdentityPoints is < 0 or > 15 ||
            result.ProcurementPoints is < 0 or > 30 ||
            result.ProductFitPoints is < 0 or > 20 ||
            result.MarketFitPoints is < 0 or > 10 ||
            result.RecencyPoints is < 0 or > 10 ||
            result.EvidencePoints is < 0 or > 10 ||
            result.ContactPoints is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(result), "One or more score components are outside the scorecard limits.");

        var componentTotal = result.IdentityPoints + result.ProcurementPoints + result.ProductFitPoints +
            result.MarketFitPoints + result.RecencyPoints + result.EvidencePoints + result.ContactPoints;
        if (componentTotal != result.Total)
            throw new ArgumentException("Score components must sum to the total.", nameof(result));
        if (result.ReviewGates is null || result.ReviewGates.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Review gates must not contain null or blank values.", nameof(result));

        return new SysCrmProspectingScore
        {
            CandidateId = candidateId,
            ScorecardVersion = result.ScorecardVersion,
            Total = result.Total,
            Priority = (int)result.Priority,
            IdentityPoints = result.IdentityPoints,
            ProcurementPoints = result.ProcurementPoints,
            ProductFitPoints = result.ProductFitPoints,
            MarketFitPoints = result.MarketFitPoints,
            RecencyPoints = result.RecencyPoints,
            EvidencePoints = result.EvidencePoints,
            ContactPoints = result.ContactPoints,
            ReviewGatesJson = JsonSerializer.Serialize(result.ReviewGates),
            EvaluatedAtUtc = result.EvaluatedAtUtc
        };
    }
}
