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
/// Real PostgreSQL state-machine coverage for W4 pause/resume. Gated by the same connection-string
/// convention as the lease tests; uses independent connections and the real AsyncLocal tenant accessor.
/// </summary>
public sealed class CrmProspectingPauseResumePostgresIntegrationTests : IDisposable
{
    private const long TenantId = 98841;
    private const long OtherTenantId = 98842;
    private readonly ICurrentTenant _tenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly SqlSugarScope _db;
    private readonly CrmProspectingRunRepository _repository;

    public static bool PostgresConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING"));

    public CrmProspectingPauseResumePostgresIntegrationTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Requires KIGER_TEST_POSTGRES_CONNECTION_STRING.");
        _db = new SqlSugarScope(new ConnectionConfig
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
        _db.CodeFirst.InitTables(typeof(SysCrmProspectingRun));
        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmProspectingRunRepository(resolver.Object);
    }

    [Fact(SkipUnless = nameof(PostgresConfigured), Skip = "Requires KIGER_TEST_POSTGRES_CONNECTION_STRING.")]
    public async Task TryPause_FromEachActiveState_PausesAndPreservesLeaseWithoutTerminalSettlement()
    {
        foreach (var source in new[]
                 {
                     CrmProspectingRunStatus.Queued,
                     CrmProspectingRunStatus.Running,
                     CrmProspectingRunStatus.WaitingForExtractions
                 })
        {
            var runId = await InsertRunAsync(source, $"pause-{source}");
            var token = Guid.NewGuid();
            try
            {
                if (source != CrmProspectingRunStatus.Queued)
                    await SetLeaseAsync(runId, source, token, 7);

                var paused = await WithTenant(TenantId, () => _repository.TryPauseAsync(runId));
                Assert.NotNull(paused);
                Assert.Equal(CrmProspectingRunStatus.Paused, paused!.Status);
                Assert.Null(paused.CompletedAt);
                if (source == CrmProspectingRunStatus.Queued)
                    Assert.Null(paused.LeaseToken);
                else
                    Assert.Equal(token, paused.LeaseToken); // lease preserved for in-flight writes
            }
            finally
            {
                await DeleteRunAsync(runId);
            }
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured), Skip = "Requires KIGER_TEST_POSTGRES_CONNECTION_STRING.")]
    public async Task TryPause_FromTerminalOrAlreadyPaused_ReturnsNull()
    {
        foreach (var source in new[] { CrmProspectingRunStatus.Succeeded, CrmProspectingRunStatus.Paused })
        {
            var runId = await InsertRunAsync(source, $"pause-reject-{source}");
            try
            {
                var paused = await WithTenant(TenantId, () => _repository.TryPauseAsync(runId));
                Assert.Null(paused);
            }
            finally
            {
                await DeleteRunAsync(runId);
            }
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured), Skip = "Requires KIGER_TEST_POSTGRES_CONNECTION_STRING.")]
    public async Task ReleaseLeaseForPause_UnderExactFencing_ClearsLeaseWithoutTerminalOrCompletedAt()
    {
        var runId = await InsertRunAsync(CrmProspectingRunStatus.Running, "pause-exit");
        var token = Guid.NewGuid();
        try
        {
            await SetLeaseAsync(runId, CrmProspectingRunStatus.Running, token, 7);
            await WithTenant(TenantId, () => _repository.TryPauseAsync(runId));

            var released = await WithTenant(TenantId, () =>
                _repository.ReleaseLeaseForPauseAsync(runId, token, 7));
            Assert.True(released);

            var after = await GetAsync(runId);
            Assert.Equal(CrmProspectingRunStatus.Paused, after.Status);
            Assert.Null(after.LeaseToken);
            Assert.Null(after.LeaseOwner);
            Assert.Null(after.LeaseExpiresAt);
            Assert.Null(after.LeaseHeartbeatAt);
            Assert.Null(after.CompletedAt);
            Assert.Equal(8, after.FencingVersion);

            // A stale fencing cannot release twice.
            var again = await WithTenant(TenantId, () =>
                _repository.ReleaseLeaseForPauseAsync(runId, token, 7));
            Assert.False(again);
        }
        finally
        {
            await DeleteRunAsync(runId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured), Skip = "Requires KIGER_TEST_POSTGRES_CONNECTION_STRING.")]
    public async Task TryResume_DerivesQueuedOrWaiting_InvalidatesLeaseAndRejectsNonPaused()
    {
        foreach (var resumeToWaiting in new[] { false, true })
        {
            var runId = await InsertRunAsync(CrmProspectingRunStatus.Paused, $"resume-{resumeToWaiting}");
            var token = Guid.NewGuid();
            try
            {
                await SetLeaseAsync(runId, CrmProspectingRunStatus.Paused, token, 3);
                var resumed = await WithTenant(TenantId, () =>
                    _repository.TryResumeAsync(runId, resumeToWaiting));
                Assert.NotNull(resumed);
                Assert.Equal(
                    resumeToWaiting ? CrmProspectingRunStatus.WaitingForExtractions : CrmProspectingRunStatus.Queued,
                    resumed!.Status);
                Assert.Null(resumed.LeaseToken);
                Assert.Null(resumed.CompletedAt);
                Assert.Equal(4, resumed.FencingVersion);

                // Resume is not idempotent: a second call on the now non-Paused Run is rejected.
                var again = await WithTenant(TenantId, () =>
                    _repository.TryResumeAsync(runId, resumeToWaiting));
                Assert.Null(again);
            }
            finally
            {
                await DeleteRunAsync(runId);
            }
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured), Skip = "Requires KIGER_TEST_POSTGRES_CONNECTION_STRING.")]
    public async Task PauseAndResume_AreTenantScoped()
    {
        var runId = await InsertRunAsync(CrmProspectingRunStatus.Running, "tenant-scope");
        try
        {
            await SetLeaseAsync(runId, CrmProspectingRunStatus.Running, Guid.NewGuid(), 1);
            var wrongTenantPause = await WithTenant(OtherTenantId, () => _repository.TryPauseAsync(runId));
            Assert.Null(wrongTenantPause);

            var paused = await WithTenant(TenantId, () => _repository.TryPauseAsync(runId));
            Assert.NotNull(paused);
            var wrongTenantResume = await WithTenant(OtherTenantId, () => _repository.TryResumeAsync(runId, false));
            Assert.Null(wrongTenantResume);
            Assert.Equal(CrmProspectingRunStatus.Paused, (await GetAsync(runId)).Status);
        }
        finally
        {
            await DeleteRunAsync(runId);
        }
    }

    private async Task<long> InsertRunAsync(CrmProspectingRunStatus status, string identity)
    {
        var runId = SnowFlakeSingle.Instance.NextId();
        using (_tenant.Change(TenantId))
        {
            var run = new SysCrmProspectingRun
            {
                TenantId = TenantId,
                CampaignId = SnowFlakeSingle.Instance.NextId(),
                StrategyId = SnowFlakeSingle.Instance.NextId(),
                StrategyVersion = 1,
                RequestKey = $"w4-{identity}-{Guid.NewGuid():N}",
                PayloadHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(identity))),
                SnapshotSearchObjective = "pause/resume integration test",
                SnapshotProviderPlanJson = "[]",
                Status = status,
                FencingVersion = 0
            };
            run.SetBasicId(runId);
            await _db.Insertable(run).ExecuteCommandAsync();
        }
        return runId;
    }

    private async Task SetLeaseAsync(long runId, CrmProspectingRunStatus status, Guid token, int fencing)
    {
        using (_tenant.Change(TenantId))
        {
            await _db.Updateable<SysCrmProspectingRun>()
                .SetColumns(run => new SysCrmProspectingRun
                {
                    Status = status,
                    LeaseOwner = "w4-test-worker",
                    LeaseToken = token,
                    LeaseHeartbeatAt = DateTimeOffset.UtcNow,
                    LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                    FencingVersion = fencing
                })
                .Where(run => run.BasicId == runId)
                .ExecuteCommandAsync();
        }
    }

    private async Task<SysCrmProspectingRun> GetAsync(long runId)
    {
        using (_tenant.Change(TenantId))
        {
            return await _db.Queryable<SysCrmProspectingRun>()
                .SingleAsync(run => run.BasicId == runId)
                ?? throw new InvalidOperationException("Run not found.");
        }
    }

    private async Task DeleteRunAsync(long runId)
    {
        using (_tenant.Change(TenantId))
        {
            await _db.Deleteable<SysCrmProspectingRun>()
                .Where(run => run.BasicId == runId)
                .ExecuteCommandAsync();
        }
    }

    private static async Task<T> WithTenant<T>(long tenantId, Func<Task<T>> action)
    {
        using (new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance).Change(tenantId))
            return await action();
    }

    private static long ScopeTenantId()
        => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;

    public void Dispose() => _db.Dispose();
}
