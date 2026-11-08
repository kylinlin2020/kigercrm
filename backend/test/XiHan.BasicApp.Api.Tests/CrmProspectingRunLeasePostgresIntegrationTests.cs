using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// PostgreSQL integration coverage for the Run lease state machine. These tests use
/// independent connections and the real AsyncLocal tenant accessor used by repositories.
/// </summary>
public sealed class CrmProspectingRunLeasePostgresIntegrationTests : IDisposable
{
    private const long TenantId = 98731;
    private const long OtherTenantId = 98732;
    private readonly ICurrentTenant _tenant1 = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly ICurrentTenant _tenant2 = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly SqlSugarScope _db1;
    private readonly SqlSugarScope _db2;
    private readonly CrmProspectingRunRepository _repository1;
    private readonly CrmProspectingRunRepository _repository2;

    public static bool PostgresConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING"));

    public CrmProspectingRunLeasePostgresIntegrationTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("PostgreSQL integration tests require KIGER_TEST_POSTGRES_CONNECTION_STRING.");

        _db1 = CreateClient(connectionString);
        _db2 = CreateClient(connectionString);
        _db1.CodeFirst.InitTables(typeof(SysCrmProspectingRun));
        _repository1 = CreateRepository(_db1);
        _repository2 = CreateRepository(_db2);
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task TryClaimLease_QueuedRun_ConcurrentWorkersHaveExactlyOneOwner()
    {
        var runId = await InsertRunAsync(TenantId, "claim-race");
        try
        {
            var outcomes = await Task.WhenAll(
                WithTenant(_tenant1, TenantId, () =>
                    _repository1.TryClaimLeaseAsync(runId, "run-worker-a", TimeSpan.FromMinutes(2))),
                WithTenant(_tenant2, TenantId, () =>
                    _repository2.TryClaimLeaseAsync(runId, "run-worker-b", TimeSpan.FromMinutes(2))));

            var claimed = Assert.Single(outcomes.Where(run => run is not null))!;
            Assert.NotEqual(Guid.Empty, claimed.LeaseToken);
            Assert.Equal(1, claimed.FencingVersion);
            Assert.Equal(CrmProspectingRunStatus.Running, claimed.Status);
        }
        finally
        {
            await DeleteRunAsync(runId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task TryClaimLease_FromAnotherTenant_CannotClaimRun()
    {
        var runId = await InsertRunAsync(TenantId, "tenant-isolation");
        try
        {
            var foreignClaim = await WithTenant(_tenant2, OtherTenantId, () =>
                _repository2.TryClaimLeaseAsync(runId, "foreign-worker", TimeSpan.FromMinutes(2)));
            Assert.Null(foreignClaim);

            var ownerClaim = await WithTenant(_tenant1, TenantId, () =>
                _repository1.TryClaimLeaseAsync(runId, "owner-worker", TimeSpan.FromMinutes(2)));
            Assert.NotNull(ownerClaim);
            Assert.Equal(TenantId, ownerClaim.TenantId);
        }
        finally
        {
            await DeleteRunAsync(runId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task RecoverExpiredLeases_ExpiredRunningRun_RequeuesForNewClaim()
    {
        var runId = await InsertRunAsync(TenantId, "expired-running-recovery");
        var token = Guid.NewGuid();
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingRun>()
                    .SetColumns(run => new SysCrmProspectingRun
                    {
                        Status = CrmProspectingRunStatus.Running,
                        LeaseOwner = "dead-worker",
                        LeaseToken = token,
                        LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                        LeaseHeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                        FencingVersion = 3
                    })
                    .Where(run => run.BasicId == runId)
                    .ExecuteCommandAsync();

                var recovered = await _repository1.RecoverExpiredLeasesAsync();
                Assert.True(recovered >= 1);

                var persisted = await _db1.Queryable<SysCrmProspectingRun>()
                    .Where(run => run.BasicId == runId)
                    .SingleAsync();
                Assert.Equal(CrmProspectingRunStatus.Queued, persisted.Status);
                Assert.Null(persisted.LeaseToken);
                Assert.Null(persisted.LeaseOwner);

                var reclaimed = await _repository1.TryClaimLeaseAsync(
                    runId, "replacement-worker", TimeSpan.FromMinutes(2));
                Assert.NotNull(reclaimed);
                Assert.Equal(4, reclaimed.FencingVersion);
            }
        }
        finally
        {
            await DeleteRunAsync(runId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task TryClaimLease_ExpiredRun_TakesOverWithNewTokenAndHigherFence()
    {
        var runId = await InsertRunAsync(TenantId, "lease-takeover");
        try
        {
            var first = await WithTenant(_tenant1, TenantId, () =>
                _repository1.TryClaimLeaseAsync(runId, "old-worker", TimeSpan.FromMinutes(2)));
            Assert.NotNull(first);

            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingRun>()
                    .SetColumns(run => run.LeaseExpiresAt == DateTimeOffset.UtcNow.AddMinutes(-1))
                    .Where(run => run.BasicId == runId)
                    .ExecuteCommandAsync();
            }

            var second = await WithTenant(_tenant2, TenantId, () =>
                _repository2.TryClaimLeaseAsync(runId, "new-worker", TimeSpan.FromMinutes(2)));

            Assert.NotNull(second);
            Assert.NotEqual(first!.LeaseToken, second!.LeaseToken);
            Assert.Equal(first.FencingVersion + 1, second.FencingVersion);

            var staleRenewal = await WithTenant(_tenant1, TenantId, () =>
                _repository1.RenewLeaseWithDatabaseClockAsync(
                    runId, first.LeaseToken!.Value, first.FencingVersion, TimeSpan.FromMinutes(2)));
            Assert.False(staleRenewal);

            var currentRenewal = await WithTenant(_tenant2, TenantId, () =>
                _repository2.RenewLeaseWithDatabaseClockAsync(
                    runId, second.LeaseToken!.Value, second.FencingVersion, TimeSpan.FromMinutes(2)));
            Assert.True(currentRenewal);
        }
        finally
        {
            await DeleteRunAsync(runId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task RecoverExpiredLeases_CancelRequestedRun_FinalizesCancelledWithoutRequeue()
    {
        var runId = await InsertRunAsync(TenantId, "expired-cancel");
        var token = Guid.NewGuid();
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingRun>()
                    .SetColumns(run => new SysCrmProspectingRun
                    {
                        Status = CrmProspectingRunStatus.CancelRequested,
                        LeaseOwner = "cancelled-worker",
                        LeaseToken = token,
                        LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                        LeaseHeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                        FencingVersion = 4
                    })
                    .Where(run => run.BasicId == runId)
                    .ExecuteCommandAsync();

                var recovered = await _repository1.RecoverExpiredLeasesAsync();
                Assert.True(recovered >= 1);

                var persisted = await _db1.Queryable<SysCrmProspectingRun>()
                    .Where(run => run.BasicId == runId)
                    .SingleAsync();
                Assert.Equal(CrmProspectingRunStatus.Cancelled, persisted.Status);
                Assert.Null(persisted.LeaseToken);
                Assert.Null(persisted.LeaseOwner);

                var queued = await _repository1.GetQueuedIdsAsync();
                Assert.DoesNotContain(runId, queued);
            }
        }
        finally
        {
            await DeleteRunAsync(runId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task RecoverInterrupted_LegacyRunningWithoutLeaseMetadata_RequeuesOnlyLegacyRow()
    {
        var runId = await InsertRunAsync(TenantId, "legacy-interrupted");
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingRun>()
                    .SetColumns(run => new SysCrmProspectingRun
                    {
                        Status = CrmProspectingRunStatus.Running,
                        LeaseOwner = null,
                        LeaseToken = null,
                        LeaseExpiresAt = null,
                        LeaseHeartbeatAt = null
                    })
                    .Where(run => run.BasicId == runId)
                    .ExecuteCommandAsync();

                var recovered = await _repository1.RecoverInterruptedAsync(DateTimeOffset.UtcNow);
                Assert.True(recovered);

                var persisted = await _db1.Queryable<SysCrmProspectingRun>()
                    .Where(run => run.BasicId == runId)
                    .SingleAsync();
                Assert.Equal(CrmProspectingRunStatus.Queued, persisted.Status);
                Assert.Null(persisted.StartedAt);
                Assert.Contains(runId, await _repository1.GetQueuedIdsAsync());
            }
        }
        finally
        {
            await DeleteRunAsync(runId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task TryLockLeaseForWrite_ShouldRejectSupersededFenceAndAcceptCurrentLiveFence()
    {
        var runId = await InsertRunAsync(TenantId, "write-fence-lock");
        try
        {
            var first = await WithTenant(_tenant1, TenantId, () =>
                _repository1.TryClaimLeaseAsync(runId, "first-writer", TimeSpan.FromMinutes(2)));
            Assert.NotNull(first);

            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingRun>()
                    .SetColumns(run => run.LeaseExpiresAt == DateTimeOffset.UtcNow.AddMinutes(-1))
                    .Where(run => run.BasicId == runId)
                    .ExecuteCommandAsync();
            }

            var replacement = await WithTenant(_tenant2, TenantId, () =>
                _repository2.TryClaimLeaseAsync(runId, "replacement-writer", TimeSpan.FromMinutes(2)));
            Assert.NotNull(replacement);
            Assert.NotEqual(first!.LeaseToken, replacement!.LeaseToken);
            Assert.True(replacement.FencingVersion > first.FencingVersion);

            using (_tenant1.Change(TenantId))
            {
                _db1.Ado.BeginTran();
                try
                {
                    var staleFenceAccepted = await _repository1.TryLockLeaseForWriteAsync(
                        runId, first.LeaseToken!.Value, first.FencingVersion);
                    var currentFenceAccepted = await _repository1.TryLockLeaseForWriteAsync(
                        runId, replacement.LeaseToken!.Value, replacement.FencingVersion);

                    Assert.False(staleFenceAccepted);
                    Assert.True(currentFenceAccepted);
                }
                finally
                {
                    _db1.Ado.RollbackTran();
                }
            }
        }
        finally
        {
            await DeleteRunAsync(runId);
        }
    }

    private async Task<long> InsertRunAsync(long tenantId, string identity)
    {
        var runId = SnowFlakeSingle.Instance.NextId();
        using (_tenant1.Change(tenantId))
        {
            await _db1.Insertable(new SysCrmProspectingRun
            {
                BasicId = runId,
                TenantId = tenantId,
                CampaignId = SnowFlakeSingle.Instance.NextId(),
                StrategyId = SnowFlakeSingle.Instance.NextId(),
                StrategyVersion = 1,
                RequestKey = $"postgres-{identity}-{Guid.NewGuid():N}",
                PayloadHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(identity))),
                SnapshotSearchObjective = "lease integration test",
                SnapshotProviderPlanJson = "[]",
                Status = CrmProspectingRunStatus.Queued,
                FencingVersion = 0
            }).ExecuteCommandAsync();
        }

        return runId;
    }

    private async Task DeleteRunAsync(long runId)
    {
        using (_tenant1.Change(TenantId))
        {
            await _db1.Deleteable<SysCrmProspectingRun>()
                .Where(run => run.BasicId == runId)
                .ExecuteCommandAsync();
        }
    }

    private static async Task<T> WithTenant<T>(ICurrentTenant tenant, long tenantId, Func<Task<T>> action)
    {
        using (tenant.Change(tenantId))
            return await action();
    }

    private static SqlSugarScope CreateClient(string connectionString)
        => new(new ConnectionConfig
        {
            DbType = DbType.PostgreSQL,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        }, db =>
        {
            db.QueryFilter.AddTableFilter<ISoftDelete>(entity => !entity.IsDeleted);
            db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(
                entity => ScopeTenantId() == entity.TenantId);
            db.Aop.DataExecuting = (_, entityInfo) =>
            {
                if (entityInfo.OperationType == DataFilterType.InsertByObject)
                {
                    entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                    entityInfo.ToCreated(EntityAuditContext.From(null, ScopeTenantId()));
                }
            };
        });

    private static long ScopeTenantId()
        => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;

    private static CrmProspectingRunRepository CreateRepository(ISqlSugarClient client)
    {
        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(client);
        return new CrmProspectingRunRepository(resolver.Object);
    }

    public void Dispose()
    {
        _db1.Dispose();
        _db2.Dispose();
    }
}
