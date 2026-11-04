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
/// Exercises the real prospecting Run repository state transitions against SQLite.
/// PostgreSQL concurrency, transaction isolation and multi-instance lease behavior remain separate gates.
/// </summary>
public sealed class CrmProspectingRunRepositorySqliteIntegrationTests : IDisposable
{
    private const long TenantId = 9843;
    private readonly string _connectionString = $"Data Source=kiger-crm-prospecting-run-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();
    private readonly CrmProspectingRunRepository _repository;

    public CrmProspectingRunRepositorySqliteIntegrationTests()
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

        _db.CodeFirst.InitTables(typeof(SysCrmProspectingRun));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmProspectingRunRepository(resolver.Object);
    }

    [Fact]
    public async Task QueuedRun_ShouldOnlyBeClaimedOnce()
    {
        using (_tenant.Change(TenantId))
        {
            var run = await CreateRunAsync("claim-once");
            var claimed = await _repository.ClaimQueuedAsync(run.BasicId, DateTimeOffset.UtcNow);

            Assert.NotNull(claimed);
            Assert.Equal(CrmProspectingRunStatus.Running, claimed.Status);
            Assert.Null(await _repository.ClaimQueuedAsync(run.BasicId, DateTimeOffset.UtcNow));

            var persisted = await _repository.GetByIdAsync(run.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingRunStatus.Running, persisted.Status);
        }
    }

    [Fact]
    public async Task CancellationRequestedBeforeCompletion_ShouldWinOverSuccess()
    {
        using (_tenant.Change(TenantId))
        {
            var run = await CreateRunAsync("cancel-before-complete");
            Assert.NotNull(await _repository.ClaimQueuedAsync(run.BasicId, DateTimeOffset.UtcNow));

            var cancellationRequested = await _repository.UpdateAsync(
                entity => new SysCrmProspectingRun { Status = CrmProspectingRunStatus.CancelRequested },
                entity => entity.BasicId == run.BasicId && entity.Status == CrmProspectingRunStatus.Running);
            Assert.True(cancellationRequested);

            var completed = await _repository.CompleteAsync(
                run.BasicId,
                "[]",
                0,
                0,
                null,
                DateTimeOffset.UtcNow,
                candidateCount: 0);

            Assert.True(completed);
            var persisted = await _repository.GetByIdAsync(run.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingRunStatus.Cancelled, persisted.Status);
            Assert.NotNull(persisted.CompletedAt);
        }
    }

    [Fact]
    public async Task InterruptedRecovery_ShouldRequeueRunningAndFinalizeCancellation()
    {
        using (_tenant.Change(TenantId))
        {
            var interrupted = await CreateRunAsync("interrupted");
            var cancelling = await CreateRunAsync("cancel-requested");
            Assert.NotNull(await _repository.ClaimQueuedAsync(interrupted.BasicId, DateTimeOffset.UtcNow));
            Assert.NotNull(await _repository.ClaimQueuedAsync(cancelling.BasicId, DateTimeOffset.UtcNow));

            var cancellationRequested = await _repository.UpdateAsync(
                entity => new SysCrmProspectingRun { Status = CrmProspectingRunStatus.CancelRequested },
                entity => entity.BasicId == cancelling.BasicId && entity.Status == CrmProspectingRunStatus.Running);
            Assert.True(cancellationRequested);

            Assert.True(await _repository.RecoverInterruptedAsync(DateTimeOffset.UtcNow));
            Assert.False(await _repository.RecoverInterruptedAsync(DateTimeOffset.UtcNow));

            var recoveredRun = await _repository.GetByIdAsync(interrupted.BasicId);
            var cancelledRun = await _repository.GetByIdAsync(cancelling.BasicId);
            Assert.NotNull(recoveredRun);
            Assert.NotNull(cancelledRun);
            Assert.Equal(CrmProspectingRunStatus.Queued, recoveredRun.Status);
            Assert.Null(recoveredRun.StartedAt);
            Assert.Equal(CrmProspectingRunStatus.Cancelled, cancelledRun.Status);
            Assert.NotNull(cancelledRun.CompletedAt);
        }
    }

    [Fact]
    public async Task RunRepository_ShouldRemainStrictlyTenantScoped()
    {
        long runId;
        using (_tenant.Change(TenantId))
        {
            var run = await CreateRunAsync("tenant-boundary");
            runId = run.BasicId;
        }

        using (_tenant.Change(TenantId + 1))
        {
            Assert.Null(await _repository.ClaimQueuedAsync(runId, DateTimeOffset.UtcNow));
            Assert.Null(await _repository.GetByIdAsync(runId));
        }

        using (_tenant.Change(TenantId))
        {
            var persisted = await _repository.GetByIdAsync(runId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingRunStatus.Queued, persisted.Status);
        }
    }

    private Task<SysCrmProspectingRun> CreateRunAsync(string requestKey)
        => _repository.AddAsync(new SysCrmProspectingRun
        {
            CampaignId = 101,
            StrategyId = 201,
            StrategyVersion = 1,
            RequestKey = requestKey,
            PayloadHash = new string('A', 64),
            SnapshotSearchObjective = "Find industrial generator distributors",
            SnapshotProductScope = "Generators",
            SnapshotProviderPlanJson = "[\"fake\"]",
            Status = CrmProspectingRunStatus.Queued
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
