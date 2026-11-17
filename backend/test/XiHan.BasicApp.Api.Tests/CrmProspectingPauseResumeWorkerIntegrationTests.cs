using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Caching.Distributed.Abstracts;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Tasks.BackgroundServices;
using XiHan.Framework.Uow;
using XiHan.Framework.Uow.Options;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// W4 end-to-end Worker coverage: drives the real private ProcessItemAsync path against
/// PostgreSQL with real fenced repositories and fake providers. Verifies that a Pause observed
/// mid-dispatch stops later providers, releases the lease without a terminal write, and that
/// Resume replays from the durable checkpoint without repeating the already-checked provider.
/// PostgreSQL is required because the provider-execution plan materializer uses native SQL.
/// </summary>
public sealed class CrmProspectingPauseResumeWorkerIntegrationTests
{
    private const long TenantId = 98911;

    public static bool PostgresConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING"));

    [Fact(SkipUnless = nameof(PostgresConfigured), Skip = "Requires KIGER_TEST_POSTGRES_CONNECTION_STRING.")]
    public async Task PauseThenResume_ShouldStopDispatchAndReplayFromCheckpoint()
    {
        var connectionString = Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING")!;
        var runId = SnowFlakeSingle.Instance.NextId();
        var tenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
        using var tenantScope = tenant.Change(TenantId);

        var db = new SqlSugarScope(new ConnectionConfig
        {
            DbType = DbType.PostgreSQL,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        }, client =>
        {
            client.QueryFilter.AddTableFilter<ISoftDelete>(e => !e.IsDeleted);
            client.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(e => ScopeTenantId() == e.TenantId);
            client.Aop.DataExecuting = (_, info) =>
            {
                if (info.OperationType == DataFilterType.InsertByObject)
                {
                    info.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                    info.ToCreated(EntityAuditContext.From(null, ScopeTenantId()));
                }
            };
        });
        db.CodeFirst.InitTables(
            typeof(SysCrmProspectingRun),
            typeof(SysCrmProspectingRunProviderExecution),
            typeof(SysCrmProspectingProviderRequest),
            typeof(SysCrmProspectingCandidate),
            typeof(SysCrmProspectingEvidence),
            typeof(SysCrmProspectingScore),
            typeof(SysCrmProspectingExtractionJob),
            typeof(SysCrmLead));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(r => r.GetClientForEntity(It.IsAny<Type>())).Returns(db);

        // The projection code opens a caller transaction for terminal provider-step
        // transitions (TryTransitionAsync checks Ado.Transaction is not null). Make the
        // fake UoW manage a real SqlSugar transaction on the shared test connection.
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.CompleteAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            if (db.Ado.Transaction is not null) db.Ado.CommitTran();
            return Task.CompletedTask;
        });
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(u => u.Dispose()).Callback(() =>
        {
            if (db.Ado.Transaction is not null) db.Ado.RollbackTran();
        });
        var uowManager = new Mock<IUnitOfWorkManager>();
        uowManager.Setup(m => m.Begin(It.IsAny<XiHanUnitOfWorkOptions>(), It.IsAny<bool>())).Returns(() =>
        {
            if (db.Ado.Transaction is null) db.Ado.BeginTran();
            return uow.Object;
        });

        ICrmProspectingRunRepository? runsFromDi = null;
        var alphaCalls = 0;
        var betaCalls = 0;
        var pauseOnce = 1;

        var alpha = new Mock<ICrmProspectingProvider>();
        alpha.SetupGet(p => p.ProviderCode).Returns("w4-alpha");
        alpha.Setup(p => p.SearchAsync(It.IsAny<CrmProspectingProviderRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (CrmProspectingProviderRequest _, CancellationToken ct) =>
            {
                Interlocked.Increment(ref alphaCalls);
                if (Interlocked.Exchange(ref pauseOnce, 0) == 1)
                {
                    Assert.NotNull(runsFromDi);
                    var paused = await runsFromDi.TryPauseAsync(runId, CancellationToken.None);
                    Assert.NotNull(paused);
                    Assert.Equal(CrmProspectingRunStatus.Paused, paused.Status);
                }
                return EmptyResult();
            });

        var beta = new Mock<ICrmProspectingProvider>();
        beta.SetupGet(p => p.ProviderCode).Returns("w4-beta");
        beta.Setup(p => p.SearchAsync(It.IsAny<CrmProspectingProviderRequest>(), It.IsAny<CancellationToken>()))
            .Returns((CrmProspectingProviderRequest _, CancellationToken _) =>
            {
                Interlocked.Increment(ref betaCalls);
                return Task.FromResult(EmptyResult());
            });

        var registry = CrmProspectingProviderRegistry.Create(
            [alpha.Object, beta.Object],
            Array.Empty<ICrmProspectingAsyncExtractionProvider>());

        var services = new ServiceCollection();
        services.AddSingleton(resolver.Object);
        services.AddSingleton<ICurrentTenant>(_ => new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance));
        services.AddSingleton(uowManager.Object);
        services.AddSingleton<ICrmProspectingRunRepository, CrmProspectingRunRepository>();
        services.AddSingleton<ICrmProspectingProviderRequestRepository, CrmProspectingProviderRequestRepository>();
        services.AddSingleton<ICrmProspectingRunProviderExecutionRepository, CrmProspectingRunProviderExecutionRepository>();
        services.AddSingleton<ICrmProspectingCandidateRepository, CrmProspectingCandidateRepository>();
        services.AddSingleton<ICrmLeadRepository, CrmLeadRepository>();
        services.AddSingleton<ICrmProspectingEvidenceRepository, CrmProspectingEvidenceRepository>();
        services.AddSingleton<ICrmProspectingScoreRepository, CrmProspectingScoreRepository>();
        services.AddSingleton<ICrmProspectingExtractionJobRepository, CrmProspectingExtractionJobRepository>();
        services.AddSingleton<CrmProspectingCandidatePersistenceService>();
        services.AddSingleton(registry);
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        var rootScope = provider.CreateScope();
        runsFromDi = rootScope.ServiceProvider.GetRequiredService<ICrmProspectingRunRepository>();

        try
        {
            await InsertRunAsync(db, runId);
            using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var worker = new CrmProspectingRunHostedService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Mock.Of<IRedisDelayQueue<CrmProspectingRunMessage>>(),
                Options.Create(new XiHanBackgroundServiceOptions()),
                loggerFactory.CreateLogger<CrmProspectingRunHostedService>());

            // First delivery: alpha is invoked and pauses the Run from inside the provider call.
            await InvokeProcessItemAsync(worker, runId);

            Assert.Equal(1, alphaCalls);
            Assert.Equal(0, betaCalls);
            var pausedRun = await runsFromDi.GetByIdAsync(runId, CancellationToken.None);
            Assert.NotNull(pausedRun);
            Assert.Equal(CrmProspectingRunStatus.Paused, pausedRun.Status);
            Assert.Null(pausedRun.LeaseToken);
            Assert.Null(pausedRun.CompletedAt);

            var executionsAfterPause = await db.Queryable<SysCrmProspectingRunProviderExecution>()
                .Where(e => e.RunId == runId)
                .ToListAsync();
            Assert.Equal(2, executionsAfterPause.Count);
            Assert.Contains(executionsAfterPause, e => e.ProviderCode == "w4-alpha" && e.State == "Checkpointed");
            Assert.Contains(executionsAfterPause, e => e.ProviderCode == "w4-beta" && e.State == "Queued");

            // Resume to Queued (no extraction jobs) and deliver again via the same worker.
            var resumed = await runsFromDi.TryResumeAsync(runId, resumeToWaiting: false, CancellationToken.None);
            Assert.NotNull(resumed);
            Assert.Equal(CrmProspectingRunStatus.Queued, resumed.Status);

            await InvokeProcessItemAsync(worker, runId);

            // alpha must not be re-invoked; its durable checkpoint is replayed instead.
            Assert.Equal(1, alphaCalls);
            Assert.Equal(1, betaCalls);
            var finalRun = await runsFromDi.GetByIdAsync(runId, CancellationToken.None);
            Assert.NotNull(finalRun);
            Assert.True(finalRun.Status is CrmProspectingRunStatus.Succeeded or CrmProspectingRunStatus.PartiallySucceeded,
                $"Unexpected terminal status {finalRun.Status}.");
        }
        finally
        {
            await CleanupAsync(db, runId);
            rootScope.Dispose();
            db.Dispose();
        }
    }

    private static CrmProspectingProviderResult EmptyResult()
        => new(Array.Empty<CrmProspectingRawRecord>(), 0, Array.Empty<string>()) { IsComplete = true };

    private static async Task InvokeProcessItemAsync(CrmProspectingRunHostedService worker, long runId)
    {
        var method = typeof(CrmProspectingRunHostedService).GetMethod(
            "ProcessItemAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ProcessItemAsync was not found on the Worker.");
        var message = new CrmProspectingRunMessage
        {
            RunId = runId,
            TenantId = TenantId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var task = (Task)method.Invoke(worker, [message, CancellationToken.None])!;
        await task;
    }

    private static async Task InsertRunAsync(SqlSugarScope db, long runId)
    {
        var run = new SysCrmProspectingRun
        {
            TenantId = TenantId,
            CampaignId = SnowFlakeSingle.Instance.NextId(),
            StrategyId = SnowFlakeSingle.Instance.NextId(),
            StrategyVersion = 1,
            RequestKey = $"w4-worker-{Guid.NewGuid():N}",
            PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()))),
            SnapshotSearchObjective = "W4 pause/resume worker integration test",
            SnapshotProviderPlanJson = "[\"w4-alpha\",\"w4-beta\"]",
            Status = CrmProspectingRunStatus.Queued,
            FencingVersion = 0
        };
        run.SetBasicId(runId);
        await db.Insertable(run).ExecuteCommandAsync();
    }

    private static async Task CleanupAsync(SqlSugarScope db, long runId)
    {
        var candidateIds = await db.Queryable<SysCrmProspectingCandidate>()
            .Where(c => c.RunId == runId)
            .Select(c => c.BasicId)
            .ToListAsync();
        if (candidateIds.Count > 0)
        {
            await db.Deleteable<SysCrmProspectingEvidence>().Where(e => candidateIds.Contains(e.CandidateId)).ExecuteCommandAsync();
            await db.Deleteable<SysCrmProspectingScore>().Where(s => candidateIds.Contains(s.CandidateId)).ExecuteCommandAsync();
        }
        await db.Deleteable<SysCrmProspectingRunProviderExecution>().Where(e => e.RunId == runId).ExecuteCommandAsync();
        await db.Deleteable<SysCrmProspectingProviderRequest>().Where(e => e.RunId == runId).ExecuteCommandAsync();
        await db.Deleteable<SysCrmProspectingCandidate>().Where(c => c.RunId == runId).ExecuteCommandAsync();
        await db.Deleteable<SysCrmProspectingExtractionJob>().Where(j => j.RunId == runId).ExecuteCommandAsync();
        await db.Deleteable<SysCrmProspectingRun>().Where(r => r.BasicId == runId).ExecuteCommandAsync();
    }

    private static long ScopeTenantId()
        => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;
}
