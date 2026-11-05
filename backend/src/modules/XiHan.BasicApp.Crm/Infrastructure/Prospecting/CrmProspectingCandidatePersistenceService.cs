using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.Uow;
using XiHan.Framework.Uow.Options;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Persists one prospect Candidate and its initial Evidence/Score as one fenced transaction.
/// Provider I/O and duplicate lookup remain outside this service.
/// </summary>
public sealed class CrmProspectingCandidatePersistenceService
{
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICrmProspectingRunRepository _runs;
    private readonly ICrmProspectingCandidateRepository _candidates;
    private readonly ICrmProspectingEvidenceRepository _evidence;
    private readonly ICrmProspectingScoreRepository _scores;

    public CrmProspectingCandidatePersistenceService(
        IUnitOfWorkManager unitOfWorkManager,
        ICrmProspectingRunRepository runs,
        ICrmProspectingCandidateRepository candidates,
        ICrmProspectingEvidenceRepository evidence,
        ICrmProspectingScoreRepository scores)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _runs = runs;
        _candidates = candidates;
        _evidence = evidence;
        _scores = scores;
    }

    public async Task<SysCrmProspectingCandidate> PersistNewAsync(
        SysCrmProspectingCandidate candidate,
        CrmProspectingRawEnvelope envelope,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(envelope);
        if (candidate.RunId <= 0) throw new ArgumentOutOfRangeException(nameof(candidate));
        if (evaluatedAtUtc.Offset != TimeSpan.Zero)
            evaluatedAtUtc = evaluatedAtUtc.ToUniversalTime();

        using var unitOfWork = _unitOfWorkManager.Begin(
            new XiHanUnitOfWorkOptions(isTransactional: true),
            requiresNew: true);

        // The worker is not an HTTP request: explicitly fence every durable child write.
        if (!await _runs.TryLockLeaseForWriteAsync(
            candidate.RunId,
            envelope.LeaseToken,
            envelope.FencingVersion,
            cancellationToken))
            throw new InvalidOperationException("The Run lease expired or was superseded before Candidate/Evidence/Score persistence.");

        var inserted = await _candidates.AddAsync(candidate, cancellationToken);
        var evidenceRows = CrmProspectingEvidenceFactory.Create(
            inserted.BasicId,
            envelope,
            evaluatedAtUtc);
        if (evidenceRows.Count > 0)
            await _evidence.AddRangeAsync(evidenceRows, cancellationToken);

        var scoreInput = new CrmProspectingScoreInput(
            HasVerifiableOrganization: false,
            HasDirectProcurementSignal: false,
            HasDirectProductFit: false,
            HasTargetMarketFit: false,
            PublishedAtUtc: envelope.Record.PublishedAtUtc?.ToUniversalTime(),
            HasTraceablePrimaryEvidence: evidenceRows.Any(evidence =>
                (evidence.EvidenceType is "direct" or "registry") &&
                evidence.VerificationStatus == "unverified" &&
                !string.IsNullOrWhiteSpace(evidence.SourceUrlHash)),
            HasPublicBusinessContact: !string.IsNullOrWhiteSpace(inserted.BusinessEmail),
            IsKnownDuplicate: inserted.Status == CrmProspectingCandidateStatus.Duplicate,
            IsRestrictedOrUnsafeSource: false);
        var scoreResult = CrmProspectingScorecard.Evaluate(scoreInput, evaluatedAtUtc);
        var scoreSnapshot = CrmProspectingScoreSnapshotFactory.Create(
            inserted.BasicId,
            scoreResult,
            evidenceRows,
            CrmProspectingRunHostedService.BuildInitialScoreIdempotencyKey(
                inserted.BasicId,
                scoreResult.ScorecardVersion));
        await _scores.AddAsync(scoreSnapshot, cancellationToken);

        await unitOfWork.CompleteAsync(cancellationToken);
        return inserted;
    }
}
