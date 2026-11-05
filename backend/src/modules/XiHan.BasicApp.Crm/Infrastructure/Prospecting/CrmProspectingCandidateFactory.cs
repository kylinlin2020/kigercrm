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
        if (!CrmProspectingExtractionPageValidator.CanAdvanceCursor(page, expectedCursor))
            throw new ArgumentException("The extraction page is invalid, incomplete, or has a non-advancing cursor.", nameof(page));
        if (items.Count > CrmProspectingExtractionPageValidator.MaxRecordsPerPage ||
            items.Count > page.Records.Count)
            throw new ArgumentOutOfRangeException(nameof(items));

        var pageIdentities = page.Records
            .Select(CrmProspectingRecordIdentity.NormalizeExternalId)
            .ToHashSet(StringComparer.Ordinal);
        var itemIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null || item.Candidate is null || item.Envelope is null ||
                item.Envelope.Record is null ||
                item.Candidate.RunId <= 0 ||
                string.IsNullOrWhiteSpace(item.Candidate.ProviderCode) ||
                !string.Equals(item.Candidate.ProviderCode, item.Envelope.ProviderCode, StringComparison.Ordinal) ||
                !page.Records.Contains(item.Envelope.Record) ||
                !string.Equals(
                    item.Candidate.ExternalId,
                    CrmProspectingRecordIdentity.NormalizeExternalId(item.Envelope.Record),
                    StringComparison.Ordinal) ||
                !itemIdentities.Add(CrmProspectingRecordIdentity.NormalizeExternalId(item.Envelope.Record)))
                throw new ArgumentException("Candidate page items must map one-to-one to unique records in the validated provider page.", nameof(items));
        }

        using var unitOfWork = _unitOfWorkManager.Begin(
            new XiHan.Framework.Uow.Options.XiHanUnitOfWorkOptions(isTransactional: true),
            requiresNew: true);

        var extraction = await _extractionJobs.GetByIdAsync(extractionJobId, cancellationToken)
            ?? throw new InvalidOperationException("The extraction job does not exist in the current tenant.");
        if (!string.Equals(extraction.Status, CrmProspectingExtractionStatus.FetchingResults.ToString(), StringComparison.Ordinal) ||
            extraction.FencingVersion != extractionFencingVersion ||
            extraction.LeaseToken != extractionLeaseToken ||
            !string.Equals(extraction.ResultCursor, expectedCursor, StringComparison.Ordinal))
            throw new InvalidOperationException("The extraction lease or expected cursor is stale.");
        if (items.Any(item => item.Candidate.RunId != extraction.RunId ||
            !string.Equals(item.Candidate.ProviderCode, extraction.ProviderCode, StringComparison.Ordinal)))
            throw new ArgumentException("Candidate page items do not belong to the extraction Run/provider.", nameof(items));

        foreach (var item in items)
        {
            var inserted = await _candidates.AddAsync(item.Candidate, cancellationToken);
            var evidenceRows = CrmProspectingEvidenceFactory.Create(
                inserted.BasicId, item.Envelope, DateTimeOffset.UtcNow);
            if (evidenceRows.Count > 0)
                await _evidence.AddRangeAsync(evidenceRows, cancellationToken);

            var scoreInput = new CrmProspectingScoreInput(
                HasVerifiableOrganization: false,
                HasDirectProcurementSignal: false,
                HasDirectProductFit: false,
                HasTargetMarketFit: false,
                PublishedAtUtc: item.Envelope.Record.PublishedAtUtc?.ToUniversalTime(),
                HasTraceablePrimaryEvidence: evidenceRows.Any(evidence =>
                    (evidence.EvidenceType is "direct" or "registry") &&
                    evidence.VerificationStatus == "unverified" &&
                    !string.IsNullOrWhiteSpace(evidence.SourceUrlHash)),
                HasPublicBusinessContact: !string.IsNullOrWhiteSpace(inserted.BusinessEmail),
                IsKnownDuplicate: inserted.Status == CrmProspectingCandidateStatus.Duplicate,
                IsRestrictedOrUnsafeSource: false);
            var evaluatedAtUtc = DateTimeOffset.UtcNow;
            var scoreResult = CrmProspectingScorecard.Evaluate(scoreInput, evaluatedAtUtc);
            var scoreSnapshot = CrmProspectingScoreSnapshotFactory.Create(
                inserted.BasicId,
                scoreResult,
                evidenceRows,
                $"initial:{inserted.BasicId}:{scoreResult.ScorecardVersion}");
            await _scores.AddAsync(scoreSnapshot, cancellationToken);
        }

        var advanced = page.IsLastPage
            ? await _extractionJobs.TryCompleteResultsAsync(
                extractionJobId,
                extractionLeaseToken,
                extractionFencingVersion,
                page.IsLastPage,
                page.IsComplete,
                page.FailureCount,
                cancellationToken,
                persistedRecordCount: items.Count,
                expectedCursor: expectedCursor)
            : await _extractionJobs.TryAdvanceCursorAsync(
                extractionJobId,
                extractionLeaseToken,
                extractionFencingVersion,
                expectedCursor,
                page.NextCursor,
                items.Count,
                cancellationToken);
        if (!advanced)
            throw new InvalidOperationException("The extraction cursor/lease changed before page commit; all page writes were rolled back.");

        await unitOfWork.CompleteAsync(cancellationToken);
        return items.Count;
    }

}
