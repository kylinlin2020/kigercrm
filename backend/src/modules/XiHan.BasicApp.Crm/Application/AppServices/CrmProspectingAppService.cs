using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Services;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;
using XiHan.Framework.Uow;
using XiHan.Framework.Uow.Options;
using XiHan.Framework.Caching.Distributed.Abstracts;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;

namespace XiHan.BasicApp.Crm.Application.AppServices;

[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Prospecting")]
public sealed class CrmProspectingAppService(
    ICrmProspectingDomainService domain,
    ICrmProspectingCampaignRepository campaigns,
    ICrmProspectingStrategyRepository strategies,
    ICrmProspectingRunRepository runs,
    ICrmProspectingCandidateRepository candidates,
    ICrmProspectingEvidenceRepository evidence,
    ICrmProspectingScoreRepository scores,
    ICrmLeadRepository leads,
    ICrmLeadDomainService leadDomain,
    IFieldSecurityService fieldSecurity,
    IUserDataScopeFilterService dataScope,
    IRedisDelayQueue<CrmProspectingRunMessage> runQueue,
    IUnitOfWorkManager unitOfWorkManager)
    : CrmApplicationService, ICrmProspectingAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmProspectingCampaignDto> CreateCampaignAsync(CrmProspectingCampaignInputDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entity = Map(input);
        entity.OwnerUserId ??= CurrentUser?.Id;
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, entity.OwnerUserId, cancellationToken);
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmProspectingCampaign), input, cancellationToken);
        return ToDto(await domain.CreateCampaignAsync(entity, cancellationToken));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmProspectingCampaignDto> UpdateCampaignAsync(CrmProspectingCampaignInputDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.BasicId is not > 0) throw new ArgumentOutOfRangeException(nameof(input.BasicId));
        var existing = await campaigns.GetByIdAsync(input.BasicId.Value, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, existing.OwnerUserId, cancellationToken);
        var updated = Map(input, input.BasicId.Value);
        updated.OwnerUserId ??= existing.OwnerUserId ?? CurrentUser?.Id;
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, updated.OwnerUserId, cancellationToken);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmProspectingCampaign), input.BasicId.Value, input, cancellationToken);
        return ToDto(await domain.UpdateCampaignAsync(updated, cancellationToken));
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmProspectingCampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken = default)
    {
        var scope = await dataScope.ResolveAccessibleUsersAsync(DateTimeOffset.UtcNow, cancellationToken);
        var items = await campaigns.GetLatestAccessibleAsync(scope.Unrestricted, scope.UserIds, 500, cancellationToken);
        return items.Select(ToDto).ToArray();
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmProspectingCampaignDto?> GetCampaignAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var entity = await campaigns.GetByIdAsync(id, cancellationToken);
        if (entity is null) return null;
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, entity.OwnerUserId, cancellationToken);
        return ToDto(entity);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmProspectingStrategyDto>> GetStrategiesAsync(long campaignId, CancellationToken cancellationToken = default)
    {
        if (campaignId <= 0) throw new ArgumentOutOfRangeException(nameof(campaignId));
        var campaign = await campaigns.GetByIdAsync(campaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        var items = await strategies.GetLatestByCampaignAsync(campaignId, 500, cancellationToken);
        return items.Select(ToDto).ToArray();
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmProspectingRunDto>> GetCampaignRunsAsync(long campaignId, CancellationToken cancellationToken = default)
    {
        if (campaignId <= 0) throw new ArgumentOutOfRangeException(nameof(campaignId));
        var campaign = await campaigns.GetByIdAsync(campaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        var items = await runs.GetLatestByCampaignAsync(campaignId, 500, cancellationToken);
        return items.Select(ToDto).ToArray();
    }

    [UnitOfWork(true, System.Data.IsolationLevel.ReadCommitted)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmProspectingStrategyDto> CreateStrategyVersionAsync(CrmProspectingStrategyInputDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.CampaignId <= 0) throw new ArgumentOutOfRangeException(nameof(input.CampaignId));
        var campaign = await campaigns.GetByIdAsync(input.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        var entity = new SysCrmProspectingStrategy
        {
            CampaignId = input.CampaignId,
            Name = input.Name,
            SearchObjective = input.SearchObjective,
            ProviderPlanJson = input.ProviderPlanJson,
            ExcludedDomainsJson = input.ExcludedDomainsJson
        };
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmProspectingStrategy), input, cancellationToken);
        return ToDto(await domain.CreateStrategyVersionAsync(entity, cancellationToken));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingStrategyDto> SubmitStrategyForApprovalAsync(long strategyId, CancellationToken cancellationToken = default)
    {
        var strategy = await strategies.GetByIdAsync(strategyId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting strategy does not exist.");
        var campaign = await campaigns.GetByIdAsync(strategy.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        return ToDto(await domain.SubmitStrategyForApprovalAsync(strategyId, cancellationToken));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingStrategyDto> DecideStrategyAsync(CrmProspectingStrategyDecisionDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var strategy = await strategies.GetByIdAsync(input.StrategyId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting strategy does not exist.");
        var campaign = await campaigns.GetByIdAsync(strategy.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        var approverId = CurrentUser?.Id;
        return ToDto(await domain.DecideStrategyAsync(input.StrategyId, input.Approve, approverId, cancellationToken));
    }

    [UnitOfWork(true, System.Data.IsolationLevel.ReadCommitted)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingCampaignDto> ActivateStrategyAsync(long strategyId, CancellationToken cancellationToken = default)
    {
        var strategy = await strategies.GetByIdAsync(strategyId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting strategy does not exist.");
        var campaign = await campaigns.GetByIdAsync(strategy.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        return ToDto(await domain.ActivateStrategyAsync(strategyId, cancellationToken));
    }

    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingRunDto> StartRunAsync(CrmProspectingRunStartDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.CampaignId <= 0) throw new ArgumentOutOfRangeException(nameof(input.CampaignId));
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RequestKey);

        try
        {
            return await StartRunInUnitOfWorkAsync(input, cancellationToken);
        }
        catch (Exception ex) when (IsRunRequestKeyUniqueViolation(ex))
        {
            // The first helper's using-scope has fully disposed and rolled back before control reaches here.
            // Retry once in a fresh UoW: the domain service will return the committed winner for an equal
            // payload hash, or reject the same RequestKey with a different payload.
            return await StartRunInUnitOfWorkAsync(input, cancellationToken);
        }
    }

    private async Task<CrmProspectingRunDto> StartRunInUnitOfWorkAsync(
        CrmProspectingRunStartDto input,
        CancellationToken cancellationToken)
    {
        using var uow = unitOfWorkManager.Begin(new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        var campaign = await campaigns.GetByIdAsync(input.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        var run = await domain.CreateRunAsync(input.CampaignId, input.RequestKey, cancellationToken);
        var message = new CrmProspectingRunMessage
        {
            RunId = run.BasicId,
            TenantId = run.TenantId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        uow.OnCompleted(() => runQueue.EnqueueAsync(message, TimeSpan.Zero));
        var result = ToDto(run);
        await uow.CompleteAsync(cancellationToken);
        return result;
    }

    private static bool IsRunRequestKeyUniqueViolation(Exception exception)
    {
        // Deliberately fail closed for other providers and unrelated unique constraints.
        // SqlSugar may wrap the provider exception, so inspect the inner-exception chain.
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var type = current.GetType();
            if (!string.Equals(type.FullName, "Npgsql.PostgresException", StringComparison.Ordinal))
                continue;

            var sqlState = type.GetProperty("SqlState")?.GetValue(current) as string;
            var constraintName = type.GetProperty("ConstraintName")?.GetValue(current) as string;
            return string.Equals(sqlState, "23505", StringComparison.Ordinal)
                && string.Equals(constraintName, "ux_crm_prospecting_run_te_rq", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmProspectingRunDto?> GetRunAsync(long runId, CancellationToken cancellationToken = default)
    {
        var run = await domain.GetRunAsync(runId, cancellationToken);
        if (run is null) return null;
        var campaign = await campaigns.GetByIdAsync(run.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        return ToDto(run);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingRunDto> CancelRunAsync(long runId, CancellationToken cancellationToken = default)
    {
        var existing = await runs.GetByIdAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        var campaign = await campaigns.GetByIdAsync(existing.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        return ToDto(await domain.CancelRunAsync(runId, cancellationToken));
    }


    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmProspectingCandidateDto>> GetRunCandidatesAsync(long runId, int? status = null, CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        var run = await runs.GetByIdAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        var campaign = await campaigns.GetByIdAsync(run.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        if (status.HasValue && !Enum.IsDefined(typeof(CrmProspectingCandidateStatus), status.Value))
            throw new ArgumentOutOfRangeException(nameof(status));
        var candidateStatus = status.HasValue ? (CrmProspectingCandidateStatus?)status.Value : null;
        var items = await candidates.GetLatestByRunAsync(runId, candidateStatus, 500, cancellationToken);
        return items.Select(ToCandidateDto).ToArray();
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmProspectingCandidateReviewDetailDto> GetCandidateReviewDetailAsync(long candidateId, CancellationToken cancellationToken = default)
    {
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));

        var candidate = await candidates.GetByIdAsync(candidateId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting candidate does not exist.");
        var run = await runs.GetByIdAsync(candidate.RunId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        var campaign = await campaigns.GetByIdAsync(run.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);

        var evidenceItems = await evidence.GetByCandidateAsync(candidate.BasicId, cancellationToken);
        var scoreItems = await scores.GetLatestByCandidateAsync(candidate.BasicId, 20, cancellationToken);

        return new CrmProspectingCandidateReviewDetailDto
        {
            Candidate = ToCandidateDto(candidate),
            Evidence = evidenceItems.Select(item => new CrmProspectingEvidenceDto
            {
                BasicId = item.BasicId,
                ProviderCode = item.ProviderCode,
                ProviderRecordId = item.ProviderRecordId,
                FieldName = item.FieldName,
                FieldValueHash = item.FieldValueHash,
                SourceUrl = item.SourceUrl,
                EvidenceType = item.EvidenceType,
                VerificationStatus = item.VerificationStatus,
                EvidenceSummary = item.EvidenceSummary,
                PublishedAtUtc = item.PublishedAtUtc,
                RetrievedAtUtc = item.RetrievedAtUtc,
                RawPayloadHash = item.RawPayloadHash
            }).ToArray(),
            ScoreHistory = scoreItems.Select(item => new CrmProspectingScoreDto
            {
                BasicId = item.BasicId,
                ScorecardVersion = item.ScorecardVersion,
                Total = item.Total,
                Priority = item.Priority,
                IdentityPoints = item.IdentityPoints,
                ProcurementPoints = item.ProcurementPoints,
                ProductFitPoints = item.ProductFitPoints,
                MarketFitPoints = item.MarketFitPoints,
                RecencyPoints = item.RecencyPoints,
                EvidencePoints = item.EvidencePoints,
                ContactPoints = item.ContactPoints,
                ReviewGatesJson = item.ReviewGatesJson,
                EvaluatedAtUtc = item.EvaluatedAtUtc
            }).ToArray()
        };
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingCandidateDto> ReviewCandidateAsync(CrmProspectingCandidateReviewDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.CandidateId <= 0) throw new ArgumentOutOfRangeException(nameof(input.CandidateId));
        var candidate = await candidates.GetByIdAsync(input.CandidateId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting candidate does not exist.");
        if (candidate.Status is not (CrmProspectingCandidateStatus.PendingReview or CrmProspectingCandidateStatus.Duplicate))
            throw new InvalidOperationException("Only pending or duplicate candidates can be reviewed.");
        if (input.Approve && candidate.Status == CrmProspectingCandidateStatus.Duplicate && !input.OverrideDuplicate)
            throw new InvalidOperationException("Duplicate candidate approval requires an explicit duplicate override.");
        var run = await runs.GetByIdAsync(candidate.RunId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        var campaign = await campaigns.GetByIdAsync(run.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmProspectingCandidate), candidate.BasicId,
            new { Status = input.Approve ? CrmProspectingCandidateStatus.Approved : CrmProspectingCandidateStatus.Rejected, input.OverrideDuplicate }, cancellationToken);
        if (!await candidates.TryReviewAsync(candidate.BasicId, input.Approve, input.OverrideDuplicate, cancellationToken))
            throw new InvalidOperationException("This candidate was already reviewed or its review state changed. Refresh before trying again.");
        candidate.Status = input.Approve ? CrmProspectingCandidateStatus.Approved : CrmProspectingCandidateStatus.Rejected;
        return ToCandidateDto(candidate);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmLeadDetailDto> ConvertApprovedCandidateToLeadAsync(long candidateId, CancellationToken cancellationToken = default)
    {
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));
        var candidate = await candidates.GetByIdAsync(candidateId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting candidate does not exist.");
        var run = await runs.GetByIdAsync(candidate.RunId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        var campaign = await campaigns.GetByIdAsync(run.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);

        // A retry after a successful commit returns the already-created Lead instead of
        // creating a second one or reporting a misleading conversion failure.
        if (candidate.Status == CrmProspectingCandidateStatus.Converted)
            return await GetConvertedCandidateLeadAsync(candidate, campaign.OwnerUserId, cancellationToken);
        if (candidate.Status != CrmProspectingCandidateStatus.Approved)
            throw new InvalidOperationException("Only an approved prospecting candidate can be converted to a Lead.");
        // The conditional database update is the concurrency boundary: exactly one caller can
        // transition Approved -> Converting. The surrounding UoW rolls the claim back if Lead creation fails.
        if (!await candidates.TryClaimForConversionAsync(candidate.BasicId, cancellationToken))
        {
            // A concurrent request may have committed the conversion while this conditional
            // claim waited on its row lock. Reload under the same tenant/UoW and return its winner.
            var latestCandidate = await candidates.GetByIdAsync(candidate.BasicId, cancellationToken)
                ?? throw new InvalidOperationException("Prospecting candidate does not exist.");
            if (latestCandidate.Status == CrmProspectingCandidateStatus.Converted)
                return await GetConvertedCandidateLeadAsync(latestCandidate, campaign.OwnerUserId, cancellationToken);
            throw new InvalidOperationException("This prospecting candidate is already being converted or is no longer approved.");
        }

        var leadInput = new CrmLeadCreateDto
        {
            CompanyName = candidate.CompanyName,
            ContactName = candidate.ContactName,
            Email = candidate.BusinessEmail,
            Website = candidate.Website,
            Source = "Prospecting",
            SourceReference = $"{candidate.ProviderCode}:{candidate.ExternalId}",
            OwnerUserId = campaign.OwnerUserId,
            Notes = candidate.Summary
        };
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmLead), leadInput, cancellationToken);
        var lead = await leadDomain.CreateAsync(new CrmLeadCreateCommand(
            leadInput.CompanyName, leadInput.ContactName, leadInput.Email, leadInput.Phone, leadInput.Website,
            leadInput.CountryRegion, leadInput.Source, leadInput.SourceReference, leadInput.OwnerUserId, leadInput.Notes), cancellationToken);
        candidate.Status = CrmProspectingCandidateStatus.Converted;
        candidate.ConvertedLeadId = lead.BasicId;
        await candidates.UpdateAsync(candidate, cancellationToken);
        return CrmLeadMapper.ToDetailDto(lead);
    }

    private async Task<CrmLeadDetailDto> GetConvertedCandidateLeadAsync(
        SysCrmProspectingCandidate candidate,
        long campaignOwnerUserId,
        CancellationToken cancellationToken)
    {
        if (candidate.ConvertedLeadId is not > 0)
            throw new InvalidOperationException("Converted prospecting candidate is missing its Lead reference.");
        var existingLead = await leads.GetByIdAsync(candidate.ConvertedLeadId.Value, cancellationToken)
            ?? throw new InvalidOperationException("Converted prospecting candidate references a missing Lead.");
        var expectedSourceReference = $"{candidate.ProviderCode}:{candidate.ExternalId}";
        if (!string.Equals(existingLead.Source, "Prospecting", StringComparison.Ordinal)
            || !string.Equals(existingLead.SourceReference, expectedSourceReference, StringComparison.Ordinal)
            || existingLead.OwnerUserId != campaignOwnerUserId)
            throw new InvalidOperationException("Converted Lead does not match its prospecting candidate source or campaign owner.");
        return CrmLeadMapper.ToDetailDto(existingLead);
    }

    private static CrmProspectingCandidateDto ToCandidateDto(SysCrmProspectingCandidate entity) => new()
    {
        BasicId = entity.BasicId,
        RunId = entity.RunId,
        ProviderCode = entity.ProviderCode,
        ExternalId = entity.ExternalId,
        Source = entity.Source,
        Title = entity.Title,
        CompanyName = entity.CompanyName,
        ContactName = entity.ContactName,
        BusinessEmail = entity.BusinessEmail,
        Website = entity.Website,
        Summary = entity.Summary,
        Status = (int)entity.Status,
        DuplicateReason = entity.DuplicateReason,
        ConvertedLeadId = entity.ConvertedLeadId,
        CreatedTime = entity.CreatedTime
    };

    private static CrmProspectingRunDto ToDto(SysCrmProspectingRun entity) => new()
    {
        BasicId = entity.BasicId,
        CampaignId = entity.CampaignId,
        StrategyId = entity.StrategyId,
        StrategyVersion = entity.StrategyVersion,
        Status = (int)entity.Status,
        CreatedTime = entity.CreatedTime,
        StartedAt = entity.StartedAt,
        CompletedAt = entity.CompletedAt,
        RawRecordCount = entity.RawRecordCount,
        CandidateCount = entity.CandidateCount,
        FailureCount = entity.FailureCount,
        FailureSummary = entity.FailureSummary
    };

    private static SysCrmProspectingCampaign Map(CrmProspectingCampaignInputDto input, long id = 0) => new()
    {
        BasicId = id,
        Name = input.Name,
        Objective = input.Objective,
        ProductScope = input.ProductScope,
        TargetMarkets = input.TargetMarkets,
        IdealCustomerProfile = input.IdealCustomerProfile,
        OwnerUserId = input.OwnerUserId
    };

    private static CrmProspectingCampaignDto ToDto(SysCrmProspectingCampaign entity) => new()
    {
        BasicId = entity.BasicId,
        Name = entity.Name,
        Objective = entity.Objective,
        ProductScope = entity.ProductScope,
        TargetMarkets = entity.TargetMarkets,
        IdealCustomerProfile = entity.IdealCustomerProfile,
        Status = (int)entity.Status,
        OwnerUserId = entity.OwnerUserId,
        ActiveStrategyVersion = entity.ActiveStrategyVersion,
        CreatedTime = entity.CreatedTime
    };

    private static CrmProspectingStrategyDto ToDto(SysCrmProspectingStrategy entity) => new()
    {
        BasicId = entity.BasicId,
        CampaignId = entity.CampaignId,
        Version = entity.Version,
        Name = entity.Name,
        SearchObjective = entity.SearchObjective,
        ProviderPlanJson = entity.ProviderPlanJson,
        ExcludedDomainsJson = entity.ExcludedDomainsJson,
        ApprovalStatus = (int)entity.ApprovalStatus,
        ApprovedBy = entity.ApprovedBy,
        ApprovedAt = entity.ApprovedAt,
        ActivatedAt = entity.ActivatedAt
    };
}
