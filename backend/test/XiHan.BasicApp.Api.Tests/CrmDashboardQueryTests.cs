// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Application.QueryServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Dashboard reports: tenant/data-scope correctness and honest aggregation.</summary>
public sealed class CrmDashboardQueryTests : IDisposable
{
    private const long TenantId = 10001;
    private const long OwnerId = 500;

    private readonly string _connectionString = $"Data Source=kiger-crm-dashboard-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly Mock<IUserDataScopeFilterService> _dataScope = new();
    private readonly CrmDashboardQueryService _service;
    private UserDataScopeFilter _scope = UserDataScopeFilter.Unlimited;

    public CrmDashboardQueryTests()
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
            typeof(SysCrmLead), typeof(SysCrmOpportunity),
            typeof(SysCrmProspectingCandidate), typeof(SysCrmProspectingRun));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _dataScope
            .Setup(value => value.ResolveAccessibleUsersAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(_scope));
        _service = new CrmDashboardQueryService(
            new CrmLeadRepository(_resolver.Object),
            new CrmOpportunityRepository(_resolver.Object),
            new CrmProspectingCandidateRepository(_resolver.Object),
            new CrmProspectingRunRepository(_resolver.Object),
            _dataScope.Object);
    }

    [Fact]
    public async Task Overview_ShouldHonorDataScopeAndCountTenantProspecting()
    {
        await SeedAsync();

        var all = await _service.GetOverviewAsync();
        Assert.Equal(2, all.LeadsTotal);
        Assert.Equal(2, all.OpportunitiesTotal);
        Assert.Equal(300m, all.OpportunityAmount);
        Assert.Equal(2, all.ProspectsTotal);
        Assert.Equal(2, all.RunsTotal);
        Assert.Equal(1, all.ActiveRuns);

        _scope = new UserDataScopeFilter(false, [OwnerId]);
        var scoped = await _service.GetOverviewAsync();
        Assert.Equal(1, scoped.LeadsTotal);
        Assert.Equal(1, scoped.OpportunitiesTotal);
        Assert.Equal(100m, scoped.OpportunityAmount);
        // Prospecting figures stay tenant-scoped (not owner-scoped).
        Assert.Equal(2, scoped.ProspectsTotal);
    }

    [Fact]
    public async Task Reports_ShouldAggregateBucketsWithShares()
    {
        await SeedAsync();
        var funnel = await _service.GetLeadFunnelAsync();
        Assert.Equal(2, funnel.Total);
        Assert.Equal(2, funnel.Buckets.Count);
        Assert.All(funnel.Buckets, bucket => Assert.Equal(50, bucket.SharePercent));

        var sources = await _service.GetProspectSourcesAsync();
        Assert.Equal(2, sources.Total);
        Assert.Contains(sources.Buckets, bucket => bucket.Key == "provider-a" && bucket.Count == 1);

        var stages = await _service.GetOpportunityStagesAsync();
        Assert.Equal(2, stages.Total);
        Assert.Equal(300m, stages.TotalAmount);
    }

    [Fact]
    public void DashboardContract_ShouldBeReadOnlyAndPermissionGuarded()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "QueryServices", "CrmDashboardQueryService.cs");
        Assert.Contains("[DynamicApi(", service, StringComparison.Ordinal);
        // 团队排行与跟进健康度是新增的两个只读方法，权限标注计数 4→6。
        Assert.Equal(6, CountOccurrences(service, "[PermissionAuthorize(CrmPermissionCodes.Read)]"));
        Assert.DoesNotContain("UpdateAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("AddAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TeamRanking_ShouldGroupByOwnerAndHonorRange()
    {
        await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        var all = await _service.GetTeamRankingAsync(null, null);
        Assert.Equal(2, all.Buckets.Count);
        Assert.Equal(4, all.Total); // two leads + two opportunities in the dataset
        Assert.Contains(all.Buckets, bucket => bucket.Key == OwnerId.ToString() && bucket.Amount == 100m);
        Assert.Contains(all.Buckets, bucket => bucket.Key == (OwnerId + 1).ToString() && bucket.Amount == 200m);

        var future = await _service.GetTeamRankingAsync(now.AddDays(1), null);
        Assert.Empty(future.Buckets);
        Assert.Equal(0, future.Total);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.GetTeamRankingAsync(now.AddDays(1), now));
    }

    [Fact]
    public async Task FollowUpHealth_ShouldBucketByLastContact()
    {
        await SeedAsync();
        var leads = new CrmLeadRepository(_resolver.Object);
        var rows = await leads.GetListAsync(_ => true);
        var fresh = rows.First(item => item.OwnerUserId == OwnerId);
        var stale = rows.First(item => item.OwnerUserId == OwnerId + 1);
        fresh.LastContactedAt = DateTime.UtcNow.AddDays(-3);
        stale.LastContactedAt = DateTime.UtcNow.AddDays(-15);
        await leads.UpdateAsync(fresh);
        await leads.UpdateAsync(stale);

        var health = await _service.GetFollowUpHealthAsync(null, null);
        Assert.Equal(2, health.Total);
        Assert.Equal(1, Assert.Single(health.Buckets.Where(bucket => bucket.Key == "contacted_7d")).Count);
        Assert.Equal(1, Assert.Single(health.Buckets.Where(bucket => bucket.Key == "contacted_30d")).Count);
        Assert.Equal(0, health.Buckets.Single(bucket => bucket.Key == "never").Count);
        Assert.Equal(4, health.Buckets.Count);
    }

    [Fact]
    public void Contract_ShouldGuardNewReportMethods()
    {
        var query = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "Contracts", "ICrmDashboardQueryService.cs");
        Assert.Contains("GetTeamRankingAsync", query, StringComparison.Ordinal);
        Assert.Contains("GetFollowUpHealthAsync", query, StringComparison.Ordinal);
        Assert.Contains("DateTimeOffset? fromUtc", query, StringComparison.Ordinal);
    }
    private async Task SeedAsync()
    {
        var leads = new CrmLeadRepository(_resolver.Object);
        var opportunities = new CrmOpportunityRepository(_resolver.Object);
        var candidates = new CrmProspectingCandidateRepository(_resolver.Object);
        var runs = new CrmProspectingRunRepository(_resolver.Object);

        await leads.AddAsync(new SysCrmLead { CompanyName = "a", Status = CrmLeadStatus.New, OwnerUserId = OwnerId });
        await leads.AddAsync(new SysCrmLead { CompanyName = "b", Status = CrmLeadStatus.Qualified, OwnerUserId = OwnerId + 1 });
        await opportunities.AddAsync(new SysCrmOpportunity { Name = "o1", Stage = CrmOpportunityStage.Qualification, Amount = 100m, OwnerUserId = OwnerId });
        await opportunities.AddAsync(new SysCrmOpportunity { Name = "o2", Stage = CrmOpportunityStage.Proposal, Amount = 200m, OwnerUserId = OwnerId + 1 });
        await candidates.AddAsync(new SysCrmProspectingCandidate { RunId = 1, ProviderCode = "provider-a", ExternalId = "e1", CompanyName = "c1" });
        await candidates.AddAsync(new SysCrmProspectingCandidate { RunId = 1, ProviderCode = "provider-b", ExternalId = "e2", CompanyName = "c2" });
        await runs.AddAsync(new SysCrmProspectingRun
        {
            CampaignId = 1, StrategyId = 1, StrategyVersion = 1, RequestKey = "r1",
            PayloadHash = new string('A', 64), SnapshotSearchObjective = "x", SnapshotProviderPlanJson = "[]",
            Status = CrmProspectingRunStatus.Running
        });
        await runs.AddAsync(new SysCrmProspectingRun
        {
            CampaignId = 1, StrategyId = 1, StrategyVersion = 1, RequestKey = "r2",
            PayloadHash = new string('B', 64), SnapshotSearchObjective = "x", SnapshotProviderPlanJson = "[]",
            Status = CrmProspectingRunStatus.Succeeded
        });
    }

    private static int CountOccurrences(string source, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    public void Dispose()
    {
        _tenantScope.Dispose();
        AsyncLocalCurrentTenantAccessor.Instance.Current = null;
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static long ScopeId() => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
