using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Exercises strategy lifecycle compare-and-swap repository operations against SQLite.
/// This is a repository integration preflight, not a substitute for PostgreSQL multi-connection tests.
/// </summary>
public sealed class CrmProspectingStrategyLifecycleSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 9851;
    private readonly string _connectionString = $"Data Source=kiger-crm-strategy-lifecycle-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();
    private readonly CrmProspectingCampaignRepository _campaigns;
    private readonly CrmProspectingStrategyRepository _strategies;

    public CrmProspectingStrategyLifecycleSqliteIntegrationTests()
    {
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
                db.QueryFilter.AddTableFilter<IMultiTenantEntity>(entity => entity.TenantId == 0 || ScopeId(tenant) == entity.TenantId);
                db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(entity => ScopeId(tenant) == entity.TenantId);
                db.Aop.DataExecuting = (_, entityInfo) =>
                {
                    if (entityInfo.OperationType == DataFilterType.InsertByObject)
                    {
                        entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                        entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                    }
                };
            });

        _db.CodeFirst.InitTables(typeof(SysCrmProspectingCampaign), typeof(SysCrmProspectingStrategy));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _campaigns = new CrmProspectingCampaignRepository(resolver.Object);
        _strategies = new CrmProspectingStrategyRepository(resolver.Object);
    }

    [Fact]
    public async Task StrategyVersionSequence_ShouldOnlyAdvanceForOneObservedValue()
    {
        using (_tenant.Change(TenantId))
        {
            var campaign = await CreateCampaignAsync("sequence-cas");
            Assert.Equal(0, campaign.StrategyVersionSequence);

            Assert.True(await _campaigns.TryAdvanceStrategyVersionAsync(campaign.BasicId, 0, 1));
            Assert.False(await _campaigns.TryAdvanceStrategyVersionAsync(campaign.BasicId, 0, 1));

            var persisted = await _campaigns.GetByIdAsync(campaign.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(1, persisted.StrategyVersionSequence);
        }
    }

    [Fact]
    public async Task ActiveStrategyPointer_ShouldOnlySwitchForOneObservedValue()
    {
        using (_tenant.Change(TenantId))
        {
            var campaign = await CreateCampaignAsync("activation-cas");

            Assert.True(await _campaigns.TrySetActiveStrategyVersionAsync(campaign.BasicId, null, 11));
            Assert.False(await _campaigns.TrySetActiveStrategyVersionAsync(campaign.BasicId, null, 12));

            var persisted = await _campaigns.GetByIdAsync(campaign.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(11, persisted.ActiveStrategyVersion);
            Assert.Equal(CrmProspectingCampaignStatus.Active, persisted.Status);
        }
    }

    [Fact]
    public async Task StrategyActivationWrites_ShouldRollbackTogether()
    {
        using (_tenant.Change(TenantId))
        {
            var campaign = await CreateCampaignAsync("activation-rollback");
            var previous = await _strategies.AddAsync(new SysCrmProspectingStrategy
            {
                CampaignId = campaign.BasicId,
                Version = 1,
                Name = "Previous",
                SearchObjective = "Previous approved objective",
                ProviderPlanJson = "[]",
                ApprovalStatus = CrmProspectingStrategyApprovalStatus.Approved,
                ActivatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
            var target = await _strategies.AddAsync(new SysCrmProspectingStrategy
            {
                CampaignId = campaign.BasicId,
                Version = 2,
                Name = "Target",
                SearchObjective = "New approved objective",
                ProviderPlanJson = "[]",
                ApprovalStatus = CrmProspectingStrategyApprovalStatus.Approved
            });

            _db.Ado.BeginTran();
            try
            {
                Assert.True(await _campaigns.TrySetActiveStrategyVersionAsync(campaign.BasicId, null, target.Version));
                Assert.True(await _strategies.ClearOtherActiveVersionsAsync(campaign.BasicId, target.BasicId));
                Assert.True(await _strategies.TryMarkActivatedAsync(campaign.BasicId, target.BasicId, DateTimeOffset.UtcNow));
            }
            finally
            {
                _db.Ado.RollbackTran();
            }

            var persistedCampaign = await _campaigns.GetByIdAsync(campaign.BasicId);
            var persistedPrevious = await _strategies.GetByIdAsync(previous.BasicId);
            var persistedTarget = await _strategies.GetByIdAsync(target.BasicId);
            Assert.NotNull(persistedCampaign);
            Assert.NotNull(persistedPrevious);
            Assert.NotNull(persistedTarget);
            Assert.Null(persistedCampaign.ActiveStrategyVersion);
            Assert.NotNull(persistedPrevious.ActivatedAt);
            Assert.Null(persistedTarget.ActivatedAt);
        }
    }

    [Fact]
    public async Task StrategyLifecycle_ShouldRemainStrictlyTenantScoped()
    {
        long campaignId;
        using (_tenant.Change(TenantId))
        {
            var campaign = await CreateCampaignAsync("tenant-boundary");
            campaignId = campaign.BasicId;
        }

        using (_tenant.Change(TenantId + 1))
        {
            Assert.False(await _campaigns.TryAdvanceStrategyVersionAsync(campaignId, 0, 1));
            Assert.False(await _campaigns.TrySetActiveStrategyVersionAsync(campaignId, null, 1));
        }

        using (_tenant.Change(TenantId))
        {
            var persisted = await _campaigns.GetByIdAsync(campaignId);
            Assert.NotNull(persisted);
            Assert.Equal(0, persisted.StrategyVersionSequence);
            Assert.Null(persisted.ActiveStrategyVersion);
        }
    }

    private Task<SysCrmProspectingCampaign> CreateCampaignAsync(string suffix)
        => _campaigns.AddAsync(new SysCrmProspectingCampaign
        {
            Name = $"Campaign {suffix}",
            Status = CrmProspectingCampaignStatus.Draft,
            StrategyVersionSequence = 0
        });

    public void Dispose()
    {
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static long ScopeId(ProbeCurrentTenant tenant) => tenant.Id ?? 0;

    private sealed class ProbeCurrentTenant : ICurrentTenant
    {
        public bool IsAvailable => Id is > 0;
        public long? Id { get; private set; }
        public string? Name { get; private set; }

        public IDisposable Change(long? id, string? name = null)
        {
            var (previousId, previousName) = (Id, Name);
            (Id, Name) = (id, name);
            return new Restore(() => (Id, Name) = (previousId, previousName));
        }

        private sealed class Restore(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }
}
