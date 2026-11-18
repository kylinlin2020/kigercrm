// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Caching.Distributed.Abstracts;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Uow;
using XiHan.Framework.Uow.Options;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// M1.2: durable Super Search missions freeze the planner output, and Run creation goes through the
/// existing approved StartRun pipeline instead of a second dispatch path. Pins idempotency by
/// request key, tenant/record authorization, provider-plan coverage, replay without a second Run,
/// and the permission metadata on all three dynamic API commands.
/// </summary>
public sealed class CrmProspectingSuperSearchMissionSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 9941;
    private const long OtherTenantId = 9942;
    private const long OwnerUserId = 99;

    private readonly string _connectionString = $"Data Source=kiger-crm-mission-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    // Repository write guards read AsyncLocalCurrentTenantAccessor.Instance.Current; use the
    // framework CurrentTenant so query filters, AOP auditing and the write guard agree.
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly Mock<IUserDataScopeFilterService> _dataScope = new();
    private readonly Mock<IFieldSecurityService> _fieldSecurity = new();
    private readonly Mock<IRedisDelayQueue<CrmProspectingRunMessage>> _runQueue = new();
    private readonly Mock<IUnitOfWorkManager> _unitOfWorkManager = new();
    private readonly CrmProspectingCampaignRepository _campaigns;
    private readonly CrmProspectingStrategyRepository _strategies;
    private readonly CrmProspectingRunRepository _runs;
    private readonly CrmProspectingSuperSearchMissionRepository _missions;
    private readonly CrmProspectingAppService _service;
    private UserDataScopeFilter _scope = UserDataScopeFilter.Unlimited;

    public CrmProspectingSuperSearchMissionSqliteIntegrationTests()
    {
        _tenantScope = _tenant.Change(TenantId);
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        var tenant = _tenant;
        _db = new SqlSugarScope(
            new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = _connectionString,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            },
            db =>
            {
                db.QueryFilter.AddTableFilter<ISoftDelete>(entity => !entity.IsDeleted);
                db.QueryFilter.AddTableFilter<IMultiTenantEntity>(entity => entity.TenantId == 0 || ScopeId() == entity.TenantId);
                db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(entity => ScopeId() == entity.TenantId);
                db.Aop.DataExecuting = (_, entityInfo) =>
                {
                    if (entityInfo.OperationType == DataFilterType.InsertByObject)
                    {
                        entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                        entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                    }
                };
            });

        _db.CodeFirst.InitTables(
            typeof(SysCrmProspectingCampaign),
            typeof(SysCrmProspectingStrategy),
            typeof(SysCrmProspectingRun),
            typeof(SysCrmProspectingSuperSearchMission));

        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _campaigns = new CrmProspectingCampaignRepository(_resolver.Object);
        _strategies = new CrmProspectingStrategyRepository(_resolver.Object);
        _runs = new CrmProspectingRunRepository(_resolver.Object);
        _missions = new CrmProspectingSuperSearchMissionRepository(_resolver.Object);
        var evidence = new CrmProspectingEvidenceRepository(_resolver.Object);
        var scores = new CrmProspectingScoreRepository(_resolver.Object);
        var leads = new CrmLeadRepository(_resolver.Object);
        var customers = new CrmCustomerRepository(_resolver.Object);
        var contacts = new CrmContactRepository(_resolver.Object);
        var extractionJobs = new CrmProspectingExtractionJobRepository(_resolver.Object);

        _dataScope
            .Setup(value => value.ResolveAccessibleUsersAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(_scope));
        // Ensure*Async<TInput> is generic with a class/new() constraint, so It.IsAnyType cannot be
        // a type argument; Moq's loose defaults already return completed tasks for these members.
        _runQueue
            .Setup(value => value.EnqueueAsync(It.IsAny<CrmProspectingRunMessage>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(value => value.CompleteAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _unitOfWorkManager
            .Setup(value => value.Begin(It.IsAny<XiHanUnitOfWorkOptions>(), It.IsAny<bool>()))
            .Returns(unitOfWork.Object);

        var provider = new Mock<ICrmProspectingProvider>();
        provider.SetupGet(value => value.ProviderCode).Returns("provider-a");

        _service = new CrmProspectingAppService(
            new CrmProspectingDomainService(_campaigns, _strategies, _runs),
            _campaigns,
            _strategies,
            _runs,
            new CrmProspectingCandidateRepository(_resolver.Object),
            evidence,
            scores,
            leads,
            new CrmLeadDomainService(leads, customers, contacts),
            _fieldSecurity.Object,
            _dataScope.Object,
            _runQueue.Object,
            _unitOfWorkManager.Object,
            extractionJobs,
            _missions,
            CrmProspectingProviderRegistry.Create([provider.Object], []),
            new Mock<IConfiguration>().Object);
    }

    [Fact]
    public async Task SaveMission_ShouldFreezeServerComputedPlan_AndBeIdempotentByRequestKey()
    {
        using (_tenant.Change(TenantId))
        {
            var campaignId = await SeedCampaignAsync();
            var input = MissionInput(campaignId, "mission-1");

            var saved = await _service.SaveSuperSearchMissionAsync(input);

            Assert.Equal(CrmProspectingSuperSearchMissionStatus.Planned, (CrmProspectingSuperSearchMissionStatus)saved.Status);
            Assert.Null(saved.RunId);
            Assert.Equal(64, saved.ScopeFingerprint.Length);
            Assert.Equal(1, saved.ProviderCount);
            Assert.Equal(2, saved.PlannedRounds);
            Assert.Equal(2, saved.Plan.Rounds.Count);
            Assert.Equal(saved.ScopeFingerprint, saved.Plan.ScopeFingerprint);
            Assert.Equal("provider-a", Assert.Single(ParseProviderCodes((await GetStoredAsync(saved.BasicId)).ProviderPlanJson)));

            // Same key + same plan is a no-op replay; a different plan under the same key is a conflict.
            var replay = await _service.SaveSuperSearchMissionAsync(MissionInput(campaignId, "mission-1"));
            Assert.Equal(saved.BasicId, replay.BasicId);
            var changed = MissionInput(campaignId, "mission-1");
            changed.Plan.Markets = ["BR"];
            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveSuperSearchMissionAsync(changed));
            Assert.Equal(1, await _db.Queryable<SysCrmProspectingSuperSearchMission>().ClearFilter().CountAsync());
        }
    }

    [Fact]
    public async Task SaveMission_ShouldRejectForeignCampaignAndOutOfScopeCaller()
    {
        using (_tenant.Change(TenantId))
        {
            var campaignId = await SeedCampaignAsync();
            var foreignCampaignId = await SeedCampaignAsync(ownerUserId: OwnerUserId + 1, tenantId: OtherTenantId);

            // A foreign-tenant campaign is not even visible in this tenant's repository.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.SaveSuperSearchMissionAsync(MissionInput(foreignCampaignId, "mission-foreign")));

            _scope = new UserDataScopeFilter(false, [OwnerUserId + 1]);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _service.SaveSuperSearchMissionAsync(MissionInput(campaignId, "mission-scope")));
        }
    }

    [Fact]
    public async Task CreateRunFromMission_ShouldReuseStartRunPipeline_AndReplayWithoutDuplicateRun()
    {
        using (_tenant.Change(TenantId))
        {
            var campaignId = await SeedCampaignAsync(status: CrmProspectingCampaignStatus.Active);
            await SeedStrategyAsync(campaignId, version: 1, providerPlanJson: "[\"provider-a\"]");
            var mission = await _service.SaveSuperSearchMissionAsync(MissionInput(campaignId, "mission-run"));

            var run = await _service.CreateRunFromSuperSearchMissionAsync(mission.BasicId);

            Assert.Equal(campaignId, run.CampaignId);
            Assert.Equal(CrmProspectingRunStatus.Queued, (CrmProspectingRunStatus)run.Status);
            var stored = await GetStoredAsync(mission.BasicId);
            Assert.Equal(CrmProspectingSuperSearchMissionStatus.Executed, stored.Status);
            Assert.Equal(run.BasicId, stored.RunId);
            Assert.Equal(1, await CountRunsAsync(mission.RequestKey));

            // Replay returns the committed Run and must never enqueue a second run.
            var replay = await _service.CreateRunFromSuperSearchMissionAsync(mission.BasicId);
            Assert.Equal(run.BasicId, replay.BasicId);
            Assert.Equal(1, await CountRunsAsync(mission.RequestKey));
        }
    }

    [Fact]
    public async Task CreateRunFromMission_ShouldRejectPlanOutsideApprovedStrategy()
    {
        using (_tenant.Change(TenantId))
        {
            var campaignId = await SeedCampaignAsync(status: CrmProspectingCampaignStatus.Active);
            await SeedStrategyAsync(campaignId, version: 1, providerPlanJson: "[\"provider-b\"]");
            var mission = await _service.SaveSuperSearchMissionAsync(MissionInput(campaignId, "mission-mismatch"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.CreateRunFromSuperSearchMissionAsync(mission.BasicId));
            Assert.Contains("not covered by the campaign's approved strategy", error.Message, StringComparison.Ordinal);
            Assert.Equal(CrmProspectingSuperSearchMissionStatus.Planned, (await GetStoredAsync(mission.BasicId)).Status);
            Assert.Equal(0, await CountRunsAsync(mission.RequestKey));
        }
    }

    [Fact]
    public void MissionCommands_ShouldCarryPipelinePermissionReuseStartRunAndShipTheMigration()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "AppServices", "CrmProspectingAppService.cs");
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "Contracts", "ICrmProspectingAppService.cs");
        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Extensions", "ServiceCollectionExtensions.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.43", "5.9.43.sql");

        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]\n    public async Task<CrmProspectingSuperSearchMissionDto> SaveSuperSearchMissionAsync", app, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]\n    public async Task<IReadOnlyList<CrmProspectingSuperSearchMissionDto>> GetSuperSearchMissionsAsync", app, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]\n    public async Task<CrmProspectingRunDto> CreateRunFromSuperSearchMissionAsync", app, StringComparison.Ordinal);
        Assert.Contains("SaveSuperSearchMissionAsync", contract, StringComparison.Ordinal);
        Assert.Contains("GetSuperSearchMissionsAsync", contract, StringComparison.Ordinal);
        Assert.Contains("CreateRunFromSuperSearchMissionAsync", contract, StringComparison.Ordinal);

        // No second dispatch path: the mission command delegates to StartRunAsync.
        Assert.Contains("await StartRunAsync(new CrmProspectingRunStartDto", app, StringComparison.Ordinal);
        Assert.Contains("ICrmProspectingSuperSearchMissionRepository missions", app, StringComparison.Ordinal);

        Assert.Contains("ICrmProspectingSuperSearchMissionRepository, CrmProspectingSuperSearchMissionRepository", registration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_prospecting_supersearch_mission", migration, StringComparison.Ordinal);
        Assert.Contains("ux_crm_prospecting_supersearch_mission_te_rq_isde", migration, StringComparison.Ordinal);
    }

    private async Task<long> SeedCampaignAsync(
        long ownerUserId = OwnerUserId,
        CrmProspectingCampaignStatus status = CrmProspectingCampaignStatus.Draft,
        int? activeVersion = 1,
        long tenantId = TenantId)
    {
        using (_tenant.Change(tenantId))
        {
            var campaign = await _campaigns.AddAsync(new SysCrmProspectingCampaign
            {
                Name = $"mission-campaign-{Guid.NewGuid():N}",
                OwnerUserId = ownerUserId,
                Status = status,
                ActiveStrategyVersion = activeVersion
            });
            return campaign.BasicId;
        }
    }

    private async Task SeedStrategyAsync(long campaignId, int version, string providerPlanJson)
    {
        using (_tenant.Change(TenantId))
        {
            await _strategies.AddAsync(new SysCrmProspectingStrategy
            {
                CampaignId = campaignId,
                Version = version,
                Name = "mission-strategy",
                SearchObjective = "find industrial buyers",
                ProviderPlanJson = providerPlanJson,
                ApprovalStatus = CrmProspectingStrategyApprovalStatus.Approved,
                ApprovedAt = DateTimeOffset.UtcNow,
                ActivatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private static CrmProspectingSuperSearchMissionCreateDto MissionInput(long campaignId, string requestKey, string provider = "provider-a") => new()
    {
        CampaignId = campaignId,
        Name = "industrial buyers plan",
        RequestKey = requestKey,
        Plan = new CrmProspectingSuperSearchPreviewInputDto
        {
            Products = ["pump"],
            Markets = ["DE"],
            CustomerTypes = ["distributor"],
            Industries = [],
            ProviderCodes = [provider],
            ExcludedTerms = [],
            ExcludedDomains = [],
            Depth = (int)CrmProspectingSuperSearchDepth.Balanced,
            RequestedRounds = 2
        }
    };

    private static string[] ParseProviderCodes(string json)
        => System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateArray()
            .Select(item => item.GetString()!).ToArray();

    private async Task<SysCrmProspectingSuperSearchMission> GetStoredAsync(long missionId)
        => await _db.Queryable<SysCrmProspectingSuperSearchMission>()
            .ClearFilter()
            .FirstAsync(mission => mission.BasicId == missionId);

    private async Task<int> CountRunsAsync(string requestKey)
        => await _db.Queryable<SysCrmProspectingRun>().ClearFilter()
            .CountAsync(run => run.RequestKey == requestKey);

    public void Dispose()
    {
        _tenantScope.Dispose();
        AsyncLocalCurrentTenantAccessor.Instance.Current = null;
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));

    private static long ScopeId() => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;
}
