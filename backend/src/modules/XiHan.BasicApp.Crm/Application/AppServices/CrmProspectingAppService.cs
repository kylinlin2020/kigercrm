using System.Text.Json;
using Microsoft.Extensions.Configuration;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Services;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
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
    IUnitOfWorkManager unitOfWorkManager,
    ICrmProspectingExtractionJobRepository extractionJobs,
    ICrmProspectingSuperSearchMissionRepository missions,
    CrmProspectingProviderRegistry providerRegistry,
    IConfiguration configuration)
    : CrmApplicationService, ICrmProspectingAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmProspectingCampaignDto> CreateCampaignAsync(CrmProspectingCampaignInputDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entity = Map(input);
        entity.OwnerUserId ??= CurrentUserId;
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
        updated.OwnerUserId ??= existing.OwnerUserId ?? CurrentUserId;
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
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
        var items = await strategies.GetLatestByCampaignAsync(campaignId, 500, cancellationToken);
        return items.Select(ToDto).ToArray();
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmProspectingRunDto>> GetCampaignRunsAsync(long campaignId, CancellationToken cancellationToken = default)
    {
        if (campaignId <= 0) throw new ArgumentOutOfRangeException(nameof(campaignId));
        var campaign = await campaigns.GetByIdAsync(campaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
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
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
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
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
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
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
        var approverId = CurrentUserId;
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
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
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

    /// <summary>
    /// Freezes planner output as a durable mission. The plan is recomputed from the submitted inputs
    /// on the server: a client-supplied snapshot is never persisted. RequestKey is the idempotency
    /// boundary shared with the eventual Run, and reusing it for a different plan is rejected.
    /// </summary>
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmProspectingSuperSearchMissionDto> SaveSuperSearchMissionAsync(
        CrmProspectingSuperSearchMissionCreateDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.CampaignId <= 0) throw new ArgumentOutOfRangeException(nameof(input.CampaignId));
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RequestKey);
        ArgumentNullException.ThrowIfNull(input.Plan);

        var campaign = await campaigns.GetByIdAsync(input.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);

        var plan = CrmProspectingSuperSearchPlanner.Create(ToSuperSearchPlannerInput(input.Plan));
        var existing = await missions.GetByRequestKeyAsync(input.RequestKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.CampaignId != input.CampaignId
                || !string.Equals(existing.ScopeFingerprint, plan.ScopeFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("The request key was already used with a different Super Search mission.");
            return CrmProspectingMapper.ToSuperSearchMissionDto(existing);
        }

        var entity = new SysCrmProspectingSuperSearchMission
        {
            CampaignId = input.CampaignId,
            Name = input.Name.Trim(),
            RequestKey = input.RequestKey.Trim(),
            ScopeFingerprint = plan.ScopeFingerprint,
            Depth = (int)input.Plan.Depth,
            PlannedRounds = plan.PlannedRounds,
            ProviderCount = plan.ProviderCount,
            ProviderPlanJson = JsonSerializer.Serialize(
                plan.Rounds.SelectMany(round => round.QueryCells).Select(cell => cell.ProviderCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
            PlanSnapshotJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Status = CrmProspectingSuperSearchMissionStatus.Planned
        };
        // The mission DTO carries no sensitive field, so no field-security policy is registered for
        // this entity; record authorization above is the enforced boundary for the frozen plan.
        return CrmProspectingMapper.ToSuperSearchMissionDto(await missions.AddAsync(entity, cancellationToken));
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmProspectingSuperSearchMissionDto>> GetSuperSearchMissionsAsync(
        long campaignId,
        CancellationToken cancellationToken = default)
    {
        if (campaignId <= 0) throw new ArgumentOutOfRangeException(nameof(campaignId));
        var campaign = await campaigns.GetByIdAsync(campaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
        var rows = await missions.GetByCampaignAsync(campaignId, 100, cancellationToken);
        return rows.Select(CrmProspectingMapper.ToSuperSearchMissionDto).ToArray();
    }

    /// <summary>
    /// Creates the Run through the same StartRun pipeline as a manual run: the approved active
    /// strategy remains the execution truth. The mission's selected providers must be covered by
    /// that strategy, so a saved plan can never launch unapproved provider work.
    /// </summary>
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingRunDto> CreateRunFromSuperSearchMissionAsync(
        long missionId,
        CancellationToken cancellationToken = default)
    {
        if (missionId <= 0) throw new ArgumentOutOfRangeException(nameof(missionId));
        var mission = await missions.GetByIdAsync(missionId, cancellationToken)
            ?? throw new InvalidOperationException("Super Search mission does not exist.");
        var campaign = await campaigns.GetByIdAsync(mission.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);

        // Replay returns the committed Run: no second Run and no new queue message.
        if (mission.RunId is > 0)
        {
            var existingRun = await runs.GetByIdAsync(mission.RunId.Value, cancellationToken)
                ?? throw new InvalidOperationException("The Super Search mission references a missing Run.");
            return ToDto(existingRun);
        }

        if (campaign.ActiveStrategyVersion is > 0)
        {
            var active = (await strategies.GetListAsync(
                strategy => strategy.CampaignId == campaign.BasicId
                    && strategy.Version == campaign.ActiveStrategyVersion.Value,
                cancellationToken)).SingleOrDefault()
                ?? throw new InvalidOperationException("The campaign active strategy version is missing or ambiguous.");
            if (active.ApprovalStatus != CrmProspectingStrategyApprovalStatus.Approved || active.ActivatedAt is null)
                throw new InvalidOperationException("The active strategy is not approved and activated.");
            var approvedCodes = ParseProviderPlan(active.ProviderPlanJson).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (ParseProviderPlan(mission.ProviderPlanJson).Any(code => !approvedCodes.Contains(code)))
                throw new InvalidOperationException("The mission provider plan is not covered by the campaign's approved strategy.");
        }

        var run = await StartRunAsync(new CrmProspectingRunStartDto
        {
            CampaignId = mission.CampaignId,
            RequestKey = mission.RequestKey
        }, cancellationToken);

        using (var uow = unitOfWorkManager.Begin(new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true))
        {
            mission.Status = CrmProspectingSuperSearchMissionStatus.Executed;
            mission.RunId = run.BasicId;
            await missions.UpdateAsync(mission, cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }
        return run;
    }

    private static CrmProspectingSuperSearchInput ToSuperSearchPlannerInput(CrmProspectingSuperSearchPreviewInputDto input) => new(
        input.Products,
        input.Markets,
        input.CustomerTypes,
        input.Industries,
        input.ProviderCodes,
        input.ExcludedTerms,
        input.ExcludedDomains,
        (CrmProspectingSuperSearchDepth)input.Depth,
        input.RequestedRounds);

    private async Task<CrmProspectingRunDto> StartRunInUnitOfWorkAsync(
        CrmProspectingRunStartDto input,
        CancellationToken cancellationToken)
    {
        using var uow = unitOfWorkManager.Begin(new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        var campaign = await campaigns.GetByIdAsync(input.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
        var run = await domain.CreateRunAsync(input.CampaignId, input.RequestKey, cancellationToken);
        var providerCodes = ParseProviderPlan(run.SnapshotProviderPlanJson);
        var asyncCodes = providerCodes
            .Where(code => providerRegistry.Asynchronous.ContainsKey(code))
            .ToArray();
        var syncCodes = providerCodes
            .Where(code => providerRegistry.Synchronous.ContainsKey(code))
            .ToArray();
        var unknownCodes = providerCodes
            .Where(code => !providerRegistry.Asynchronous.ContainsKey(code)
                && !providerRegistry.Synchronous.ContainsKey(code))
            .ToArray();

        if (unknownCodes.Length > 0)
            throw new InvalidOperationException(
                $"The approved provider plan contains unregistered provider(s): {string.Join(", ", unknownCodes)}.");
        if (asyncCodes.Length > 0 && syncCodes.Length > 0)
            throw new InvalidOperationException(
                "Mixed synchronous/asynchronous provider plans remain unsupported; no provider work was queued.");
        if (asyncCodes.Length > 0 &&
            !string.Equals(
                configuration["CrmProspecting:AsyncExecution:Enabled"],
                "true",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Asynchronous prospecting execution is disabled until automated and PostgreSQL acceptance tests pass.");

        if (asyncCodes.Length > 0)
        {
            if (run.Status == CrmProspectingRunStatus.Queued)
            {
                if (!await runs.TrySetWaitingForExtractionsAsync(run.BasicId, cancellationToken))
                    throw new InvalidOperationException(
                        "The Run was claimed by another worker before asynchronous extraction intents could be committed.");
                run.Status = CrmProspectingRunStatus.WaitingForExtractions;
            }
            else if (run.Status != CrmProspectingRunStatus.WaitingForExtractions)
            {
                if (run.Status is CrmProspectingRunStatus.Succeeded
                    or CrmProspectingRunStatus.PartiallySucceeded
                    or CrmProspectingRunStatus.Failed
                    or CrmProspectingRunStatus.Cancelled)
                    return ToDto(run);
                throw new InvalidOperationException(
                    "The asynchronous Run is already owned by another execution path and cannot be rescheduled.");
            }

            var excludedDomains = ParseExcludedDomainsForExtraction(run.SnapshotExcludedDomainsJson);
            var request = CrmProspectingProviderRequestFactory.Create(run, excludedDomains);
            var createdAt = DateTimeOffset.UtcNow;
            foreach (var providerCode in asyncCodes)
            {
                var fingerprint = CrmProspectingRequestFingerprint.Create(providerCode, request);
                await extractionJobs.CreateSubmissionIntentAsync(
                    run.BasicId, providerCode, fingerprint, createdAt, cancellationToken);
            }
        }
        else
        {
            if (run.Status == CrmProspectingRunStatus.WaitingForExtractions)
                throw new InvalidOperationException(
                    "The persisted Run is waiting for asynchronous providers, but its current provider registry no longer matches that plan.");
            if (run.Status == CrmProspectingRunStatus.Queued)
            {
                var message = new CrmProspectingRunMessage
                {
                    RunId = run.BasicId,
                    TenantId = run.TenantId,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                uow.OnCompleted(() => runQueue.EnqueueAsync(message, TimeSpan.Zero));
            }
        }

        var result = ToDto(run);
        await uow.CompleteAsync(cancellationToken);
        return result;
    }

    private static string[] ParseProviderPlan(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The immutable Run provider plan is not a JSON array.");
        var codes = document.RootElement.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null)
            .ToArray();
        if (codes.Length == 0 || codes.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("The immutable Run provider plan contains an invalid provider code.");
        return codes.Select(code => code!).ToArray();
    }

    private static IReadOnlyList<string> ParseExcludedDomainsForExtraction(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The immutable excluded-domain snapshot is not a JSON array.");
        var values = document.RootElement.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null)
            .ToArray();
        if (values.Length > 500 || values.Any(value => string.IsNullOrWhiteSpace(value) || value!.Length > 253))
            throw new InvalidOperationException("The immutable excluded-domain snapshot exceeds its bounds.");
        return values.Select(value => value!).ToArray();
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
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
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
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
        return ToDto(await domain.CancelRunAsync(runId, cancellationToken));
    }

    [UnitOfWork(true, System.Data.IsolationLevel.ReadCommitted)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingRunDto> PauseRunAsync(long runId, CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        var existing = await runs.GetByIdAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        var campaign = await campaigns.GetByIdAsync(existing.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);

        // TryPauseAsync only accepts Queued/Running/WaitingForExtractions and preserves the lease;
        // returning null means a terminal state, a concurrent Resume/Cancel, or already Paused.
        var paused = await runs.TryPauseAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException(
                "The Run is not in a pausable state (Queued, Running, or WaitingForExtractions).");
        return ToDto(paused);
    }

    [UnitOfWork(true, System.Data.IsolationLevel.ReadCommitted)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingRunDto> ResumeRunAsync(long runId, CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        var existing = await runs.GetByIdAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        var campaign = await campaigns.GetByIdAsync(existing.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);

        // Derive the resume target from the ledger: a Run with extraction children returns to
        // WaitingForExtractions (coordinator re-aggregates, even if all children already finished);
        // a synchronous Run returns to Queued and is re-enqueued via the existing run queue.
        var resumeToWaiting = await runs.HasExtractionJobsAsync(runId, cancellationToken);
        var resumed = await runs.TryResumeAsync(runId, resumeToWaiting, cancellationToken)
            ?? throw new InvalidOperationException("The Run is not Paused and cannot be resumed.");
        if (!resumeToWaiting)
        {
            var message = new CrmProspectingRunMessage
            {
                RunId = resumed.BasicId,
                TenantId = resumed.TenantId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var ambientUnitOfWork = unitOfWorkManager.Current
                ?? throw new InvalidOperationException("Resume requires an ambient UnitOfWork to re-enqueue the Run.");
            ambientUnitOfWork.OnCompleted(() => runQueue.EnqueueAsync(message, TimeSpan.Zero));
        }
        return ToDto(resumed);
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
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);

        var evidenceItems = await evidence.GetByCandidateAsync(candidate.BasicId, cancellationToken);
        var scoreItems = await scores.GetLatestByCandidateAsync(candidate.BasicId, 20, cancellationToken);

        return new CrmProspectingCandidateReviewDetailDto
        {
            Candidate = CrmProspectingMapper.ToCandidateDto(candidate),
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
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmProspectingCandidate), candidate.BasicId,
            new { Status = input.Approve ? CrmProspectingCandidateStatus.Approved : CrmProspectingCandidateStatus.Rejected, input.OverrideDuplicate }, cancellationToken);
        if (!await candidates.TryReviewAsync(candidate.BasicId, input.Approve, input.OverrideDuplicate, cancellationToken))
            throw new InvalidOperationException("This candidate was already reviewed or its review state changed. Refresh before trying again.");
        candidate.Status = input.Approve ? CrmProspectingCandidateStatus.Approved : CrmProspectingCandidateStatus.Rejected;
        return CrmProspectingMapper.ToCandidateDto(candidate);
    }

    [UnitOfWork(true, System.Data.IsolationLevel.ReadCommitted)]
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
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);

        // A retry after a successful commit returns the already-created Lead instead of
        // creating a second one or reporting a misleading conversion failure.
        if (candidate.Status == CrmProspectingCandidateStatus.Converted)
            return await GetConvertedCandidateLeadAsync(candidate, campaign.OwnerUserId ?? 0, cancellationToken);
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
                return await GetConvertedCandidateLeadAsync(latestCandidate, campaign.OwnerUserId ?? 0, cancellationToken);
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
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmLead), leadInput, cancellationToken);
        var lead = await leadDomain.CreateAsync(new CrmLeadCreateCommand(
            leadInput.CompanyName, leadInput.ContactName, leadInput.Email, leadInput.Phone, leadInput.Website,
            leadInput.CountryRegion, leadInput.Source, leadInput.SourceReference, leadInput.OwnerUserId, leadInput.Notes), cancellationToken);
        candidate.Status = CrmProspectingCandidateStatus.Converted;
        candidate.ConvertedLeadId = lead.BasicId;
        await candidates.UpdateAsync(candidate, cancellationToken);
        return CrmLeadMapper.ToDetailDto(lead);
    }

    /// <summary>
    /// Maximum ids accepted by one bulk call. Bounding the list keeps each item's independent
    /// transaction predictable and stops one request from staging an unbounded transaction fan-out.
    /// </summary>
    private const int MaxBulkCandidateCount = 200;

    /// <summary>
    /// Bulk operations deliberately carry no [UnitOfWork]: every item opens its own
    /// <c>requiresNew</c> unit of work, so one item's failure rolls back only that item while
    /// already-committed items stay committed (the same programmatic boundary as
    /// <c>StartRunInUnitOfWorkAsync</c>). Each item reuses the single-item method, which applies
    /// the full state machine and record authorization; permission is enforced once at the
    /// pipeline entry by <c>PermissionAuthorize</c>.
    /// </summary>
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingBulkResultDto> BulkReviewCandidatesAsync(CrmProspectingCandidateBulkReviewDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var candidateIds = ValidateBulkCandidateIds(input.CandidateIds);
        var items = new List<CrmProspectingBulkItemResultDto>(candidateIds.Length);
        foreach (var candidateId in candidateIds)
        {
            using var uow = unitOfWorkManager.Begin(new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            try
            {
                await ReviewCandidateAsync(new CrmProspectingCandidateReviewDto
                {
                    CandidateId = candidateId,
                    Approve = input.Approve,
                    OverrideDuplicate = input.OverrideDuplicate
                }, cancellationToken);
                await uow.CompleteAsync(cancellationToken);
                items.Add(CreateBulkItem(candidateId, CrmProspectingBulkOutcome.Success));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation is a request-level stop, not a per-item business failure:
                // rethrow so remaining items are not attempted and the failure is not hidden.
                throw;
            }
            catch (Exception ex)
            {
                // Honest per-item failure: the disposed unit of work rolls this item back and
                // the loop continues with the next item's own unit of work.
                items.Add(CreateBulkItem(candidateId, CrmProspectingBulkOutcome.Failed, CrmProspectingDiagnosticSanitizer.Sanitize(ex.Message, 200)));
            }
        }
        return new CrmProspectingBulkResultDto { Items = items };
    }

    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmProspectingBulkResultDto> BulkConvertCandidatesAsync(CrmProspectingCandidateBulkDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var candidateIds = ValidateBulkCandidateIds(input.CandidateIds);
        var items = new List<CrmProspectingBulkItemResultDto>(candidateIds.Length);
        foreach (var candidateId in candidateIds)
        {
            using var uow = unitOfWorkManager.Begin(new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            try
            {
                // Report an already-converted candidate as an idempotent skip instead of
                // presenting the existing Lead as if this call had converted it.
                var candidate = await candidates.GetByIdAsync(candidateId, cancellationToken)
                    ?? throw new InvalidOperationException("Prospecting candidate does not exist.");
                if (candidate.Status == CrmProspectingCandidateStatus.Converted)
                {
                    await uow.CompleteAsync(cancellationToken);
                    items.Add(CreateBulkItem(candidateId, CrmProspectingBulkOutcome.Skipped,
                        "Candidate is already converted to a Lead.", candidate.ConvertedLeadId));
                    continue;
                }

                var lead = await ConvertApprovedCandidateToLeadAsync(candidateId, cancellationToken);
                await uow.CompleteAsync(cancellationToken);
                items.Add(CreateBulkItem(candidateId, CrmProspectingBulkOutcome.Success, leadId: lead.BasicId));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                items.Add(CreateBulkItem(candidateId, CrmProspectingBulkOutcome.Failed, CrmProspectingDiagnosticSanitizer.Sanitize(ex.Message, 200)));
            }
        }
        return new CrmProspectingBulkResultDto { Items = items };
    }

    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingBulkResultDto> ShortlistCandidatesAsync(CrmProspectingCandidateBulkDto input, CancellationToken cancellationToken = default)
        => await SetCandidatesShortlistAsync(input, shortlisted: true, cancellationToken);

    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingBulkResultDto> UnshortlistCandidatesAsync(CrmProspectingCandidateBulkDto input, CancellationToken cancellationToken = default)
        => await SetCandidatesShortlistAsync(input, shortlisted: false, cancellationToken);

    private async Task<CrmProspectingBulkResultDto> SetCandidatesShortlistAsync(
        CrmProspectingCandidateBulkDto input,
        bool shortlisted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var candidateIds = ValidateBulkCandidateIds(input.CandidateIds);
        var items = new List<CrmProspectingBulkItemResultDto>(candidateIds.Length);
        foreach (var candidateId in candidateIds)
        {
            using var uow = unitOfWorkManager.Begin(new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            try
            {
                await SetCandidateShortlistAsync(candidateId, shortlisted, cancellationToken);
                await uow.CompleteAsync(cancellationToken);
                items.Add(CreateBulkItem(candidateId, CrmProspectingBulkOutcome.Success));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                items.Add(CreateBulkItem(candidateId, CrmProspectingBulkOutcome.Failed, CrmProspectingDiagnosticSanitizer.Sanitize(ex.Message, 200)));
            }
        }
        return new CrmProspectingBulkResultDto { Items = items };
    }

    private async Task SetCandidateShortlistAsync(long candidateId, bool shortlisted, CancellationToken cancellationToken)
    {
        var candidate = await candidates.GetByIdAsync(candidateId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting candidate does not exist.");
        var run = await runs.GetByIdAsync(candidate.RunId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        var campaign = await campaigns.GetByIdAsync(run.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);

        // Shortlisting is orthogonal to the review state machine: an item already in the
        // requested state is an idempotent success, otherwise only the timestamp column moves
        // through a column-targeted update (a concurrent review of the row is never overwritten).
        if ((candidate.ShortlistedAt is not null) == shortlisted) return;
        await candidates.SetShortlistedAsync(
            candidateId,
            shortlisted ? DateTimeOffset.UtcNow : null,
            cancellationToken);
    }

    private static long[] ValidateBulkCandidateIds(IReadOnlyList<long> candidateIds)
    {
        ArgumentNullException.ThrowIfNull(candidateIds);
        if (candidateIds.Count == 0)
            throw new ArgumentException("At least one candidate id is required.", nameof(candidateIds));
        if (candidateIds.Count > MaxBulkCandidateCount)
            throw new ArgumentOutOfRangeException(nameof(candidateIds), $"A bulk operation accepts at most {MaxBulkCandidateCount} candidates.");
        foreach (var candidateId in candidateIds)
        {
            if (candidateId <= 0)
                throw new ArgumentOutOfRangeException(nameof(candidateIds), "Candidate ids must be positive.");
        }
        // De-duplicate while preserving the caller's order: a repeated id would otherwise be
        // reported twice, the second occurrence failing only because the first one already moved it.
        return candidateIds.Distinct().ToArray();
    }

    private static CrmProspectingBulkItemResultDto CreateBulkItem(
        long candidateId,
        CrmProspectingBulkOutcome outcome,
        string? reason = null,
        long? leadId = null) => new()
    {
        CandidateId = candidateId,
        Outcome = (int)outcome,
        Reason = reason,
        LeadId = leadId
    };

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

    private static SysCrmProspectingCampaign Map(CrmProspectingCampaignInputDto input, long id = 0)
    {
        var entity = new SysCrmProspectingCampaign
        {
            Name = input.Name,
            Objective = input.Objective,
            ProductScope = input.ProductScope,
            TargetMarkets = input.TargetMarkets,
            IdealCustomerProfile = input.IdealCustomerProfile,
            OwnerUserId = input.OwnerUserId
        };
        if (id > 0) entity.SetBasicId(id);
        return entity;
    }

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
