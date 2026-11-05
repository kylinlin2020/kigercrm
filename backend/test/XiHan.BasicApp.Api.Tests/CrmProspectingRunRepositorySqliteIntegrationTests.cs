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
    public async Task RunLeaseMetadata_ShouldRoundTripThroughSqlSugarCodeFirstMapping()
    {
        using (_tenant.Change(TenantId))
        {
            var now = DateTimeOffset.UtcNow;
            var token = Guid.NewGuid();
            var run = await _repository.AddAsync(new SysCrmProspectingRun
            {
                CampaignId = 101,
                StrategyId = 201,
                StrategyVersion = 1,
                RequestKey = "lease-metadata-roundtrip",
                PayloadHash = new string('B', 64),
                SnapshotSearchObjective = "Lease mapping test",
                SnapshotProviderPlanJson = "[\"fake\"]",
                Status = CrmProspectingRunStatus.Running,
                LeaseOwner = "worker-test-instance",
                LeaseToken = token,
                LeaseExpiresAt = now.AddMinutes(2),
                LeaseHeartbeatAt = now,
                FencingVersion = 7
            });

            var persisted = await _repository.GetByIdAsync(run.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal("worker-test-instance", persisted.LeaseOwner);
            Assert.Equal(token, persisted.LeaseToken);
            Assert.Equal(now.AddMinutes(2), persisted.LeaseExpiresAt);
            Assert.Equal(now, persisted.LeaseHeartbeatAt);
            Assert.Equal(7, persisted.FencingVersion);
        }
    }

    [Fact]
    public async Task FencedLeaseOperations_ShouldRemainTenantScoped()
    {
        var now = DateTimeOffset.UtcNow;
        var token = Guid.NewGuid();
        long tenantRunId;
        using (_tenant.Change(TenantId))
        {
            var run = await CreateLeasedRunAsync("tenant-fenced-lease", token, 3, now.AddMinutes(1));
            tenantRunId = run.BasicId;
        }

        using (_tenant.Change(TenantId + 1))
        {
            Assert.False(await _repository.RenewLeaseAsync(
                tenantRunId, token, 3, now, TimeSpan.FromMinutes(1)));
            Assert.False(await _repository.CompleteFencedAsync(
                tenantRunId, token, 3, now, "[]", 0, 0, null, now));
            Assert.False(await _repository.FailFencedAsync(
                tenantRunId, token, 3, now, "cross tenant", now));
        }

        using (_tenant.Change(TenantId))
        {
            var persisted = await _repository.GetByIdAsync(tenantRunId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingRunStatus.Running, persisted.Status);
            Assert.Equal(token, persisted.LeaseToken);
            Assert.Equal(3, persisted.FencingVersion);
        }
    }

    [Fact]
    public async Task RenewLease_ShouldRequireExactTokenVersionAndUnexpiredOwnership()
    {
        using (_tenant.Change(TenantId))
        {
            var now = DateTimeOffset.UtcNow;
            var token = Guid.NewGuid();
            var run = await CreateLeasedRunAsync("lease-renew-guard", token, 4, now.AddMinutes(1));

            Assert.False(await _repository.RenewLeaseAsync(run.BasicId, Guid.NewGuid(), 4, now, TimeSpan.FromMinutes(2)));
            Assert.False(await _repository.RenewLeaseAsync(run.BasicId, token, 3, now, TimeSpan.FromMinutes(2)));
            Assert.True(await _repository.RenewLeaseAsync(run.BasicId, token, 4, now, TimeSpan.FromMinutes(2)));

            var persisted = await _repository.GetByIdAsync(run.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(now, persisted.LeaseHeartbeatAt);
            Assert.Equal(now.AddMinutes(2), persisted.LeaseExpiresAt);
            Assert.Equal(token, persisted.LeaseToken);
            Assert.Equal(4, persisted.FencingVersion);

            Assert.False(await _repository.RenewLeaseAsync(run.BasicId, token, 4, now.AddMinutes(3), TimeSpan.FromMinutes(2)));
        }
    }

    [Fact]
    public async Task FencedTerminalTransitions_ShouldRejectStaleOwnerAndClearLeaseOnSuccess()
    {
        using (_tenant.Change(TenantId))
        {
            var now = DateTimeOffset.UtcNow;
            var token = Guid.NewGuid();
            var run = await CreateLeasedRunAsync("fenced-terminal", token, 9, now.AddMinutes(1));

            Assert.False(await _repository.CompleteFencedAsync(
                run.BasicId, Guid.NewGuid(), 9, now, "[]", 0, 0, null, now));
            Assert.False(await _repository.FailFencedAsync(
                run.BasicId, token, 8, now, "stale worker", now));
            Assert.True(await _repository.CompleteFencedAsync(
                run.BasicId, token, 9, now, "[]", 0, 0, null, now));

            var persisted = await _repository.GetByIdAsync(run.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingRunStatus.Succeeded, persisted.Status);
            Assert.Null(persisted.LeaseOwner);
            Assert.Null(persisted.LeaseToken);
            Assert.Null(persisted.LeaseExpiresAt);
            Assert.Null(persisted.LeaseHeartbeatAt);
            Assert.Equal(9, persisted.FencingVersion);
            Assert.False(await _repository.FailFencedAsync(
                run.BasicId, token, 9, now, "late failure", now));
        }
    }

    [Fact]
    public async Task FailFenced_ShouldPersistFailureAndClearLeaseMetadata()
    {
        using (_tenant.Change(TenantId))
        {
            var now = DateTimeOffset.UtcNow;
            var token = Guid.NewGuid();
            var run = await CreateLeasedRunAsync("fenced-failure", token, 5, now.AddMinutes(1));

            Assert.True(await _repository.FailFencedAsync(
                run.BasicId, token, 5, now, new string('x', 2500), now));

            var persisted = await _repository.GetByIdAsync(run.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingRunStatus.Failed, persisted.Status);
            Assert.Equal(1, persisted.FailureCount);
            Assert.Equal(2000, persisted.FailureSummary?.Length);
            Assert.Null(persisted.LeaseOwner);
            Assert.Null(persisted.LeaseToken);
            Assert.Null(persisted.LeaseExpiresAt);
            Assert.Null(persisted.LeaseHeartbeatAt);
        }
    }

    [Fact]
    public async Task CancelFenced_ShouldRequireCurrentLeaseAndPreserveCancellationIntent()
    {
        using (_tenant.Change(TenantId))
        {
            var now = DateTimeOffset.UtcNow;
            var token = Guid.NewGuid();
            var run = await CreateLeasedRunAsync("fenced-cancel", token, 6, now.AddMinutes(1));
            Assert.True(await _repository.UpdateAsync(
                entity => new SysCrmProspectingRun { Status = CrmProspectingRunStatus.CancelRequested },
                entity => entity.BasicId == run.BasicId && entity.Status == CrmProspectingRunStatus.Running));

            Assert.False(await _repository.CancelFencedAsync(
                run.BasicId, Guid.NewGuid(), 6, now, now));
            Assert.False(await _repository.CancelFencedAsync(
                run.BasicId, token, 5, now, now));
            Assert.True(await _repository.CancelFencedAsync(
                run.BasicId, token, 6, now, now));

            var persisted = await _repository.GetByIdAsync(run.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingRunStatus.Cancelled, persisted.Status);
            Assert.Null(persisted.LeaseOwner);
            Assert.Null(persisted.LeaseToken);
            Assert.Null(persisted.LeaseExpiresAt);
            Assert.Null(persisted.LeaseHeartbeatAt);
        }
    }

    [Fact]
    public async Task FencedTerminalTransitions_ShouldRejectExpiredLease()
    {
        using (_tenant.Change(TenantId))
        {
            var now = DateTimeOffset.UtcNow;
            var token = Guid.NewGuid();
            var run = await CreateLeasedRunAsync("expired-fenced-terminal", token, 2, now.AddSeconds(-1));

            Assert.False(await _repository.CompleteFencedAsync(
                run.BasicId, token, 2, now, "[]", 0, 0, null, now));
            Assert.False(await _repository.FailFencedAsync(
                run.BasicId, token, 2, now, "expired", now));
            var persisted = await _repository.GetByIdAsync(run.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingRunStatus.Running, persisted.Status);
        }
    }

    [Fact]
    public async Task GetByRequestKey_ShouldReturnOnlyTheCurrentTenantsRun()
    {
        long firstTenantRunId;
        using (_tenant.Change(TenantId))
        {
            var run = await CreateRunAsync("shared-request-key");
            firstTenantRunId = run.BasicId;
            Assert.Equal(firstTenantRunId, (await _repository.GetByRequestKeyAsync("shared-request-key"))?.BasicId);
        }

        using (_tenant.Change(TenantId + 1))
        {
            Assert.Null(await _repository.GetByRequestKeyAsync("shared-request-key"));
            var otherTenantRun = await CreateRunAsync("shared-request-key");
            Assert.NotEqual(firstTenantRunId, otherTenantRun.BasicId);
            Assert.Equal(otherTenantRun.BasicId, (await _repository.GetByRequestKeyAsync("shared-request-key"))?.BasicId);
        }

        using (_tenant.Change(TenantId))
            Assert.Equal(firstTenantRunId, (await _repository.GetByRequestKeyAsync("shared-request-key"))?.BasicId);
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
    public async Task Failure_ShouldBeTerminalAndBoundFailureSummaryToColumnLimit()
    {
        using (_tenant.Change(TenantId))
        {
            var run = await CreateRunAsync("failure-terminal");
            Assert.NotNull(await _repository.ClaimQueuedAsync(run.BasicId, DateTimeOffset.UtcNow));

            var summary = new string('x', 2500);
            Assert.True(await _repository.FailAsync(run.BasicId, summary, DateTimeOffset.UtcNow));

            var persisted = await _repository.GetByIdAsync(run.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingRunStatus.Failed, persisted.Status);
            Assert.Equal(1, persisted.FailureCount);
            Assert.Equal(2000, persisted.FailureSummary?.Length);

            Assert.False(await _repository.FailAsync(run.BasicId, "second failure", DateTimeOffset.UtcNow));
            Assert.False(await _repository.CompleteAsync(
                run.BasicId, "[]", 0, 0, null, DateTimeOffset.UtcNow,
                candidateCount: 0));

            var unchanged = await _repository.GetByIdAsync(run.BasicId);
            Assert.NotNull(unchanged);
            Assert.Equal(CrmProspectingRunStatus.Failed, unchanged.Status);
            Assert.Equal(summary[..2000], unchanged.FailureSummary);
        }
    }

    [Fact]
    public async Task FailAfterCancellationRequest_ShouldFinalizeCancelledNotFailed()
    {
        using (_tenant.Change(TenantId))
        {
            var run = await CreateRunAsync("fail-after-cancel");
            Assert.NotNull(await _repository.ClaimQueuedAsync(run.BasicId, DateTimeOffset.UtcNow));

            Assert.True(await _repository.UpdateAsync(
                entity => new SysCrmProspectingRun { Status = CrmProspectingRunStatus.CancelRequested },
                entity => entity.BasicId == run.BasicId && entity.Status == CrmProspectingRunStatus.Running));

            Assert.True(await _repository.FailAsync(run.BasicId, "shutdown", DateTimeOffset.UtcNow));

            var persisted = await _repository.GetByIdAsync(run.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingRunStatus.Cancelled, persisted.Status);
            Assert.Equal(0, persisted.FailureCount);
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
            var succeeded = await CreateRunAsync("already-succeeded");
            var failed = await CreateRunAsync("already-failed");
            var cancelled = await CreateRunAsync("already-cancelled");
            Assert.NotNull(await _repository.ClaimQueuedAsync(interrupted.BasicId, DateTimeOffset.UtcNow));
            Assert.NotNull(await _repository.ClaimQueuedAsync(cancelling.BasicId, DateTimeOffset.UtcNow));

            var cancellationRequested = await _repository.UpdateAsync(
                entity => new SysCrmProspectingRun { Status = CrmProspectingRunStatus.CancelRequested },
                entity => entity.BasicId == cancelling.BasicId && entity.Status == CrmProspectingRunStatus.Running);
            Assert.True(cancellationRequested);
            Assert.True(await _repository.UpdateAsync(
                entity => new SysCrmProspectingRun { Status = CrmProspectingRunStatus.Succeeded },
                entity => entity.BasicId == succeeded.BasicId && entity.Status == CrmProspectingRunStatus.Queued));
            Assert.True(await _repository.UpdateAsync(
                entity => new SysCrmProspectingRun { Status = CrmProspectingRunStatus.Failed },
                entity => entity.BasicId == failed.BasicId && entity.Status == CrmProspectingRunStatus.Queued));
            Assert.True(await _repository.UpdateAsync(
                entity => new SysCrmProspectingRun { Status = CrmProspectingRunStatus.Cancelled },
                entity => entity.BasicId == cancelled.BasicId && entity.Status == CrmProspectingRunStatus.Queued));

            Assert.True(await _repository.RecoverInterruptedAsync(DateTimeOffset.UtcNow));
            Assert.False(await _repository.RecoverInterruptedAsync(DateTimeOffset.UtcNow));

            var recoveredRun = await _repository.GetByIdAsync(interrupted.BasicId);
            var cancelledRun = await _repository.GetByIdAsync(cancelling.BasicId);
            var succeededRun = await _repository.GetByIdAsync(succeeded.BasicId);
            var failedRun = await _repository.GetByIdAsync(failed.BasicId);
            var alreadyCancelledRun = await _repository.GetByIdAsync(cancelled.BasicId);
            Assert.NotNull(recoveredRun);
            Assert.NotNull(cancelledRun);
            Assert.NotNull(succeededRun);
            Assert.NotNull(failedRun);
            Assert.NotNull(alreadyCancelledRun);
            Assert.Equal(CrmProspectingRunStatus.Queued, recoveredRun.Status);
            Assert.Null(recoveredRun.StartedAt);
            Assert.Equal(CrmProspectingRunStatus.Cancelled, cancelledRun.Status);
            Assert.NotNull(cancelledRun.CompletedAt);
            Assert.Equal(CrmProspectingRunStatus.Succeeded, succeededRun.Status);
            Assert.Equal(CrmProspectingRunStatus.Failed, failedRun.Status);
            Assert.Equal(CrmProspectingRunStatus.Cancelled, alreadyCancelledRun.Status);
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


    [Theory]
    [InlineData(CrmProspectingRunStatus.Running)]
    [InlineData(CrmProspectingRunStatus.Succeeded)]
    [InlineData(CrmProspectingRunStatus.PartiallySucceeded)]
    [InlineData(CrmProspectingRunStatus.Failed)]
    [InlineData(CrmProspectingRunStatus.CancelRequested)]
    [InlineData(CrmProspectingRunStatus.Cancelled)]
    public async Task ClaimQueued_ShouldRejectCancelledRunningAndTerminalStates(CrmProspectingRunStatus initialStatus)
    {
        using (_tenant.Change(TenantId))
        {
            var run = await CreateRunAsync($"claim-locked-{(int)initialStatus}");
            Assert.True(await _repository.UpdateAsync(
                entity => new SysCrmProspectingRun { Status = initialStatus },
                entity => entity.BasicId == run.BasicId && entity.Status == CrmProspectingRunStatus.Queued));

            Assert.Null(await _repository.ClaimQueuedAsync(run.BasicId, DateTimeOffset.UtcNow));

            var persisted = await _repository.GetByIdAsync(run.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(initialStatus, persisted.Status);
        }
    }

    private Task<SysCrmProspectingRun> CreateLeasedRunAsync(
        string requestKey,
        Guid leaseToken,
        long fencingVersion,
        DateTimeOffset leaseExpiresAt)
        => _repository.AddAsync(new SysCrmProspectingRun
        {
            CampaignId = 101,
            StrategyId = 201,
            StrategyVersion = 1,
            RequestKey = requestKey,
            PayloadHash = new string('C', 64),
            SnapshotSearchObjective = "Lease fencing test",
            SnapshotProviderPlanJson = "[\"fake\"]",
            Status = CrmProspectingRunStatus.Running,
            LeaseOwner = "worker-test-instance",
            LeaseToken = leaseToken,
            LeaseExpiresAt = leaseExpiresAt,
            LeaseHeartbeatAt = DateTimeOffset.UtcNow,
            FencingVersion = fencingVersion
        });

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
