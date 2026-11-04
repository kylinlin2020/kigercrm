using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;
using XiHan.Framework.Uow;
using XiHan.Framework.Caching.Distributed.Abstracts;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;

namespace XiHan.BasicApp.Crm.Application.AppServices;

[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Prospecting")]
public sealed class CrmProspectingAppService(
    ICrmProspectingDomainService domain,
    ICrmProspectingCampaignRepository campaigns,
    ICrmProspectingStrategyRepository strategies,
    ICrmProspectingRunRepository runs,
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
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        var entity = Map(input);
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
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmProspectingCampaign), input.BasicId.Value, input, cancellationToken);
        return ToDto(await domain.UpdateCampaignAsync(Map(input, input.BasicId.Value), cancellationToken));
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
        var items = await strategies.GetListAsync(x => x.CampaignId == campaignId, cancellationToken);
        return items.OrderByDescending(x => x.Version).Select(ToDto).ToArray();
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmProspectingRunDto>> GetCampaignRunsAsync(long campaignId, CancellationToken cancellationToken = default)
    {
        if (campaignId <= 0) throw new ArgumentOutOfRangeException(nameof(campaignId));
        var campaign = await campaigns.GetByIdAsync(campaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId, cancellationToken);
        var items = await runs.GetListAsync(x => x.CampaignId == campaignId, cancellationToken);
        return items.OrderByDescending(x => x.CreatedTime).Select(ToDto).ToArray();
    }

    [UnitOfWork(true)]
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

    [UnitOfWork(true)]
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

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingRunDto> StartRunAsync(CrmProspectingRunStartDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.CampaignId <= 0) throw new ArgumentOutOfRangeException(nameof(input.CampaignId));
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RequestKey);
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
        if (unitOfWorkManager.Current is { } uow)
            uow.OnCompleted(() => runQueue.EnqueueAsync(message, TimeSpan.Zero));
        else
            await runQueue.EnqueueAsync(message, TimeSpan.Zero, cancellationToken);
        return ToDto(run);
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
