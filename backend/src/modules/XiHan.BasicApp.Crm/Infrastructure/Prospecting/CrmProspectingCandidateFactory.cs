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

    public CrmProspectingCandidatePersistenceService(
        XiHan.Framework.Uow.IUnitOfWorkManager unitOfWorkManager,
        XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingRunRepository runs,
        XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingCandidateRepository candidates,
        XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingEvidenceRepository evidence,
        XiHan.BasicApp.Crm.Domain.Repositories.ICrmProspectingScoreRepository scores)
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
}
