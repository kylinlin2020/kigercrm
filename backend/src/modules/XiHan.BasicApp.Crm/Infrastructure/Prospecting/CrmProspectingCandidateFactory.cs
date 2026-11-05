using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Services;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Shared, deterministic Candidate field mapping for synchronous Runs and asynchronous result pages.
/// Duplicate classification is supplied by the caller because it requires tenant-scoped repository reads.
/// </summary>
public static class CrmProspectingCandidateFactory
{
    public static string? NormalizeEmail(CrmProspectingRawRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var email = string.IsNullOrWhiteSpace(record.BusinessEmail) ? null : record.BusinessEmail.Trim();
        if (email?.Length > 254) email = null;
        return CrmLeadDuplicateMatcher.NormalizeEmail(email);
    }

    public static string? NormalizeWebsiteHost(CrmProspectingRawRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var website = string.IsNullOrWhiteSpace(record.Website) ? null : record.Website.Trim();
        if (website?.Length > 500) website = website[..500];
        return CrmLeadDuplicateMatcher.NormalizeDomain(website);
    }

    public static SysCrmProspectingCandidate Create(
        long runId,
        string providerCode,
        CrmProspectingRawRecord record,
        string externalId,
        string companyName,
        string? normalizedEmail,
        string? websiteHost,
        bool isDuplicate,
        long? duplicateLeadId,
        string? duplicateReason)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (string.IsNullOrWhiteSpace(providerCode)) throw new ArgumentException("Provider code is required.", nameof(providerCode));
        if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("External identity is required.", nameof(externalId));
        if (string.IsNullOrWhiteSpace(companyName)) throw new ArgumentException("Company name is required.", nameof(companyName));

        var email = string.IsNullOrWhiteSpace(record.BusinessEmail) ? null : record.BusinessEmail.Trim();
        if (email?.Length > 254) email = null;
        var website = string.IsNullOrWhiteSpace(record.Website) ? null : record.Website.Trim();
        if (website?.Length > 500) website = website[..500];
        var source = record.Source?.Trim();
        var title = record.Title?.Trim();
        var contactName = record.ContactName?.Trim();
        var summary = record.Summary?.Trim();

        return new SysCrmProspectingCandidate
        {
            RunId = runId,
            ProviderCode = providerCode.Trim(),
            ExternalId = externalId,
            Source = string.IsNullOrWhiteSpace(source) ? null : source[..Math.Min(source.Length, 200)],
            Title = string.IsNullOrWhiteSpace(title) ? null : title[..Math.Min(title.Length, 300)],
            CompanyName = companyName.Trim()[..Math.Min(companyName.Trim().Length, 200)],
            ContactName = string.IsNullOrWhiteSpace(contactName) ? null : contactName[..Math.Min(contactName.Length, 200)],
            BusinessEmail = email,
            NormalizedEmail = normalizedEmail,
            Website = website,
            WebsiteHost = websiteHost,
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary[..Math.Min(summary.Length, 5000)],
            Status = isDuplicate ? CrmProspectingCandidateStatus.Duplicate : CrmProspectingCandidateStatus.PendingReview,
            DuplicateLeadId = duplicateLeadId,
            DuplicateReason = duplicateReason
        };
    }
}


/// <summary>One already-normalized candidate and its provider evidence envelope from an extraction page.</summary>
public sealed record CrmProspectingCandidatePageItem(
    SysCrmProspectingCandidate Candidate,
    CrmProspectingRawEnvelope Envelope);

/// <summary>
/// Shared fenced persistence boundary for Candidate, Evidence and initial Score.
/// Duplicate lookup and unique-key race recovery remain with the caller.
/// </summary>
public sealed class CrmProspectingCandidatePersistenceService
{
    private readonly XiHan.Framework.Uow.IUnitOfWorkManager _unitOfWorkManager;
    private readonly XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingRunRepository _runs;
    private readonly XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingCandidateRepository _candidates;
    private readonly XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingEvidenceRepository _evidence;
    private readonly XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingScoreRepository _scores;
    private readonly XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingExtractionJobRepository _extractionJobs;

    public CrmProspectingCandidatePersistenceService(
        XiHan.Framework.Uow.IUnitOfWorkManager unitOfWorkManager,
        XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingRunRepository runs,
        XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingCandidateRepository candidates,
        XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingEvidenceRepository evidence,
        XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingScoreRepository scores,
        XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingExtractionJobRepository extractionJobs)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _runs = runs;
        _candidates = candidates;
        _evidence = evidence;
        _scores = scores;
        _extractionJobs = extractionJobs;
    }

    public async Task<SysCrmProspectingCandidate> PersistNewAsync(
        SysCrmProspectingCandidate candidate,
        CrmProspectingRawEnvelope envelope,
        Guid leaseToken,
        long fencingVersion,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(envelope);
        if (candidate.RunId <= 0) throw new ArgumentOutOfRangeException(nameof(candidate));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Lease token is required.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
        evaluatedAtUtc = evaluatedAtUtc.ToUniversalTime();

        using var unitOfWork = _unitOfWorkManager.Begin(
            new XiHan.Framework.Uow.Options.XiHanUnitOfWorkOptions(isTransactional: true),
            requiresNew: true);
        if (!await _runs.TryLockLeaseForWriteAsync(candidate.RunId, leaseToken, fencingVersion, cancellationToken))
            throw new InvalidOperationException("The Run lease expired or was superseded before Candidate/Evidence/Score persistence.");

        var inserted = await _candidates.AddAsync(candidate, cancellationToken);
        var evidenceRows = CrmProspectingEvidenceFactory.Create(inserted.BasicId, envelope, evaluatedAtUtc);
        if (evidenceRows.Count > 0)
            await _evidence.AddRangeAsync(evidenceRows, cancellationToken);

        var scoreInput = new CrmProspectingScoreInput(
            HasVerifiableOrganization: false,
            HasDirectProcurementSignal: false,
            HasDirectProductFit: false,
            HasTargetMarketFit: false,
            PublishedAtUtc: envelope.Record.PublishedAtUtc?.ToUniversalTime(),
            HasTraceablePrimaryEvidence: evidenceRows.Any(e =>
                (e.EvidenceType is "direct" or "registry") &&
                e.VerificationStatus == "unverified" &&
                !string.IsNullOrWhiteSpace(e.SourceUrlHash)),
            HasPublicBusinessContact: !string.IsNullOrWhiteSpace(inserted.BusinessEmail),
            IsKnownDuplicate: inserted.Status == CrmProspectingCandidateStatus.Duplicate,
            IsRestrictedOrUnsafeSource: false);
        var scoreResult = CrmProspectingScorecard.Evaluate(scoreInput, evaluatedAtUtc);
        var scoreSnapshot = CrmProspectingScoreSnapshotFactory.Create(
            inserted.BasicId,
            scoreResult,
            evidenceRows,
            $"initial:{inserted.BasicId}:{scoreResult.ScorecardVersion}");
        await _scores.AddAsync(scoreSnapshot, cancellationToken);

        await unitOfWork.CompleteAsync(cancellationToken);
        return inserted;
    }
    /// <summary>
    /// Persists one validated extraction page and advances its cursor (or completes the final page)
    /// in the same PostgreSQL transaction. A failed fencing/CAS update throws so the entire page rolls back.
    /// </summary>
    public async Task<int> PersistPageAndAdvanceAsync(
        long extractionJobId,
        Guid extractionLeaseToken,
        long extractionFencingVersion,
        string? expectedCursor,
        CrmProspectingExtractionPage page,
        IReadOnlyList<CrmProspectingCandidatePageItem> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(items);
        if (extractionJobId <= 0) throw new ArgumentOutOfRangeException(nameof(extractionJobId));
        if (extractionLeaseToken == Guid.Empty) throw new ArgumentException("Extraction lease token is required.", nameof(extractionLeaseToken));
        if (extractionFencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(extractionFencingVersion));
        if (!CrmProspectingExtractionPageValidator.CanCommitPage(page, expectedCursor))
            throw new ArgumentException("The extraction page is invalid, incomplete, or has a non-advancing cursor.", nameof(page));
        if (items.Count > CrmProspectingExtractionPageValidator.MaxRecordsPerPage ||
            items.Count > page.Records.Count)
            throw new ArgumentOutOfRangeException(nameof(items));

        using var unitOfWork = _unitOfWorkManager.Begin(
            new XiHan.Framework.Uow.Options.XiHanUnitOfWorkOptions(isTransactional: true),
            requiresNew: true);
        var extraction = await _extractionJobs.GetByIdAsync(extractionJobId, cancellationToken)
            ?? throw new InvalidOperationException("The extraction job does not exist in the current tenant.");
        if (!string.Equals(extraction.Status, CrmProspectingExtractionStatus.FetchingResults.ToString(), StringComparison.Ordinal) ||
            extraction.LeaseToken != extractionLeaseToken ||
            extraction.FencingVersion != extractionFencingVersion ||
            !string.Equals(extraction.ResultCursor, expectedCursor, StringComparison.Ordinal))
            throw new InvalidOperationException("The extraction lease, lifecycle state, or expected cursor changed before page persistence.");

        var pageIdentities = page.Records
            .Select(CrmProspectingRecordIdentity.NormalizeExternalId)
            .ToHashSet(StringComparer.Ordinal);
        var itemIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null || item.Candidate is null || item.Envelope is null || item.Envelope.Record is null)
                throw new ArgumentException("Every extraction candidate page item must contain a candidate and raw record.", nameof(items));
            var identity = CrmProspectingRecordIdentity.NormalizeExternalId(item.Envelope.Record);
            if (!pageIdentities.Contains(identity) || !itemIdentities.Add(identity))
                throw new ArgumentException("Candidate page items must map one-to-one to distinct records in the fetched page.", nameof(items));
            if (item.Candidate.RunId != extraction.RunId ||
                !string.Equals(item.Candidate.ProviderCode, extraction.ProviderCode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.Envelope.ProviderCode, extraction.ProviderCode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.Candidate.ExternalId, identity, StringComparison.Ordinal))
                throw new ArgumentException("Candidate, provider envelope, extraction job, and normalized record identity must agree.", nameof(items));
        }

        foreach (var item in items)
            await PersistCandidateOrRepairAsync(item, cancellationToken);

        bool advanced;
        if (page.IsLastPage && extraction.ResultFailureCount + page.FailureCount > 0)
        {
            advanced = await _extractionJobs.TryFailIncompleteResultsAsync(
                extractionJobId,
                extractionLeaseToken,
                extractionFencingVersion,
                page.IsLastPage,
                page.IsComplete,
                page.FailureCount,
                items.Count,
                expectedCursor,
                cancellationToken);
        }
        else if (page.IsLastPage)
        {
            advanced = await _extractionJobs.TryCompleteResultsAsync(
                extractionJobId,
                extractionLeaseToken,
                extractionFencingVersion,
                page.IsLastPage,
                page.IsComplete,
                page.FailureCount,
                cancellationToken,
                persistedRecordCount: items.Count,
                expectedCursor: expectedCursor);
        }
        else
        {
            advanced = await _extractionJobs.TryAdvanceCursorAsync(
                extractionJobId,
                extractionLeaseToken,
                extractionFencingVersion,
                expectedCursor,
                page.NextCursor,
                items.Count,
                cancellationToken,
                page.FailureCount);
        }
        if (!advanced)
            throw new InvalidOperationException("The extraction cursor/lease changed before page commit; all page writes were rolled back.");

        await unitOfWork.CompleteAsync(cancellationToken);
        return items.Count;
    }

    /// <summary>
    /// Idempotently persists a page candidate. Existing same-Run identities receive only missing
    /// immutable evidence and a missing initial score; human review/conversion state is never rewritten.
    /// Caller owns the surrounding page transaction and extraction lease fence.
    /// </summary>
    private async Task PersistCandidateOrRepairAsync(
        CrmProspectingCandidatePageItem item,
        CancellationToken cancellationToken)
    {
        var candidate = item.Candidate;
        var envelope = item.Envelope;
        var externalId = CrmProspectingRecordIdentity.NormalizeExternalId(envelope.Record);
        var existing = await _candidates.GetByIdentityAsync(
            candidate.RunId, candidate.ProviderCode, externalId, cancellationToken);
        var target = existing ?? await _candidates.AddAsync(candidate, cancellationToken);

        var expectedEvidence = CrmProspectingEvidenceFactory.Create(
            target.BasicId, envelope, DateTimeOffset.UtcNow);
        var persistedEvidence = existing is null
            ? Array.Empty<SysCrmProspectingEvidence>()
            : await _evidence.GetByCandidateAsync(target.BasicId, cancellationToken);
        var persistedKeys = persistedEvidence.Select(evidence => (
                evidence.ProviderCode,
                evidence.ProviderRecordId,
                evidence.FieldName,
                evidence.SourceUrlHash,
                evidence.RawPayloadHash))
            .ToHashSet();
        var missingEvidence = expectedEvidence
            .Where(evidence => !persistedKeys.Contains((
                evidence.ProviderCode,
                evidence.ProviderRecordId,
                evidence.FieldName,
                evidence.SourceUrlHash,
                evidence.RawPayloadHash)))
            .ToArray();
        if (missingEvidence.Length > 0)
            await _evidence.AddRangeAsync(missingEvidence, cancellationToken);

        var existingScores = existing is null
            ? Array.Empty<SysCrmProspectingScore>()
            : (await _scores.GetLatestByCandidateAsync(target.BasicId, 1, cancellationToken)).ToArray();
        if (existingScores.Length > 0) return;

        var completeEvidence = persistedEvidence.Concat(missingEvidence).ToArray();
        var scoreEvidence = completeEvidence
            .OrderByDescending(evidence =>
                (evidence.EvidenceType is "direct" or "registry") &&
                !string.IsNullOrWhiteSpace(evidence.SourceUrlHash))
            .ThenBy(evidence => evidence.ProviderCode, StringComparer.Ordinal)
            .ThenBy(evidence => evidence.ProviderRecordId, StringComparer.Ordinal)
            .ThenBy(evidence => evidence.FieldName, StringComparer.Ordinal)
            .ThenBy(evidence => evidence.SourceUrlHash, StringComparer.Ordinal)
            .ThenBy(evidence => evidence.RawPayloadHash, StringComparer.Ordinal)
            .Take(32)
            .ToArray();
        var evaluatedAtUtc = DateTimeOffset.UtcNow;
        var scoreInput = new CrmProspectingScoreInput(
            HasVerifiableOrganization: false,
            HasDirectProcurementSignal: false,
            HasDirectProductFit: false,
            HasTargetMarketFit: false,
            PublishedAtUtc: completeEvidence
                .Where(evidence => evidence.PublishedAtUtc.HasValue)
                .Select(evidence => evidence.PublishedAtUtc)
                .Max(),
            HasTraceablePrimaryEvidence: completeEvidence.Any(evidence =>
                (evidence.EvidenceType is "direct" or "registry") &&
                evidence.VerificationStatus == "unverified" &&
                !string.IsNullOrWhiteSpace(evidence.SourceUrlHash)),
            HasPublicBusinessContact: !string.IsNullOrWhiteSpace(target.BusinessEmail),
            IsKnownDuplicate: target.Status == CrmProspectingCandidateStatus.Duplicate,
            IsRestrictedOrUnsafeSource: false);
        var scoreResult = CrmProspectingScorecard.Evaluate(scoreInput, evaluatedAtUtc);
        var scoreSnapshot = CrmProspectingScoreSnapshotFactory.Create(
            target.BasicId,
            scoreResult,
            scoreEvidence,
            $"initial:{target.BasicId}:{scoreResult.ScorecardVersion}");
        await _scores.AddAsync(scoreSnapshot, cancellationToken);
    }

}
