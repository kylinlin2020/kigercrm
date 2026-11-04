using System.Text.Json;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Maps deterministic score output to immutable persistence without changing the score.</summary>
public static class CrmProspectingScoreSnapshotFactory
{
    public static SysCrmProspectingScore Create(long candidateId, CrmProspectingScoreResult result, IReadOnlyList<SysCrmProspectingEvidence>? evidence = null, string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));
        if (string.IsNullOrWhiteSpace(result.ScorecardVersion) || result.ScorecardVersion.Length > 80)
            throw new ArgumentException("Scorecard version must contain 1-80 characters.", nameof(result));
        if (idempotencyKey is { Length: > 160 } || (idempotencyKey is not null && string.IsNullOrWhiteSpace(idempotencyKey)))
            throw new ArgumentException("Idempotency key must be null or contain 1-160 non-blank characters.", nameof(idempotencyKey));
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
        if (result.ReviewGates.Count > 50 || result.ReviewGates.Any(gate => gate.Length > 120))
            throw new ArgumentException("Review gates are limited to 50 entries of at most 120 characters each.", nameof(result));

        var evidenceRows = evidence ?? Array.Empty<SysCrmProspectingEvidence>();
        if (evidenceRows.Count > 100)
            throw new ArgumentException("A score snapshot can reference at most 100 evidence rows.", nameof(evidence));
        var evidenceReferencesJson = JsonSerializer.Serialize(evidenceRows
            .OrderBy(row => row.ProviderCode, StringComparer.Ordinal)
            .ThenBy(row => row.ProviderRecordId, StringComparer.Ordinal)
            .ThenBy(row => row.FieldName, StringComparer.Ordinal)
            .ThenBy(row => row.SourceUrlHash, StringComparer.Ordinal)
            .ThenBy(row => row.RawPayloadHash, StringComparer.Ordinal)
            .Select(row => new
            {
                row.ProviderCode,
                row.ProviderRecordId,
                row.FieldName,
                row.SourceUrlHash,
                row.RawPayloadHash,
                row.EvidenceType,
                row.VerificationStatus
            }));
        if (System.Text.Encoding.UTF8.GetByteCount(evidenceReferencesJson) > 32_000)
            throw new ArgumentException("Serialized evidence references exceed the 32 KB score snapshot limit.", nameof(evidence));

        var reviewGatesJson = JsonSerializer.Serialize(result.ReviewGates);
        if (System.Text.Encoding.UTF8.GetByteCount(reviewGatesJson) > 10_000)
            throw new ArgumentException("Serialized review gates exceed the 10 KB snapshot limit.", nameof(result));

        return new SysCrmProspectingScore
        {
            CandidateId = candidateId,
            IdempotencyKey = idempotencyKey,
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
            ReviewGatesJson = reviewGatesJson,
            EvaluatedAtUtc = result.EvaluatedAtUtc,
            EvidenceReferencesJson = evidenceReferencesJson
        };
    }
}
