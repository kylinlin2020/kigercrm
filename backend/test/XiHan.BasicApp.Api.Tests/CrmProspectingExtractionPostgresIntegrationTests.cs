using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// PostgreSQL-backed tests for the durable extraction claim path. The real framework
/// CurrentTenant is required because the repository reads AsyncLocalCurrentTenantAccessor
/// directly; a hand-written ICurrentTenant fake does not establish that context.
/// </summary>
public sealed class CrmProspectingExtractionPostgresIntegrationTests : IDisposable
{
    private const long TenantId = 98631;
    private const long OtherTenantId = 98632;
    private readonly ICurrentTenant _tenant1 = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly ICurrentTenant _tenant2 = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly SqlSugarScope _db1;
    private readonly SqlSugarScope _db2;
    private readonly CrmProspectingExtractionJobRepository _repository1;
    private readonly CrmProspectingExtractionJobRepository _repository2;

    public static bool PostgresConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING"));

    public CrmProspectingExtractionPostgresIntegrationTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("PostgreSQL integration tests require KIGER_TEST_POSTGRES_CONNECTION_STRING.");

        _db1 = CreateClient(connectionString);
        _db2 = CreateClient(connectionString);
        _db1.CodeFirst.InitTables(typeof(SysCrmProspectingExtractionJob));
        _repository1 = CreateRepository(_db1);
        _repository2 = CreateRepository(_db2);
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task ClaimDue_TwoIndependentClients_ExactlyOneWinsAndReturnsFencedLease()
    {
        var jobId = await InsertDueJobAsync(TenantId, "claim-race");
        var results = await RaceAsync(
            () => WithTenant(_tenant1, TenantId, () => _repository1.ClaimDueAsync("pg-worker-a", TimeSpan.FromMinutes(2), 1)),
            () => WithTenant(_tenant2, TenantId, () => _repository2.ClaimDueAsync("pg-worker-b", TimeSpan.FromMinutes(2), 1)));

        Assert.Equal(1, results.Sum(rows => rows.Count));
        var claimed = Assert.Single(results.SelectMany(rows => rows));
        Assert.Equal(jobId, claimed.BasicId);
        Assert.NotEqual(Guid.Empty, claimed.LeaseToken);
        Assert.Equal(1, claimed.FencingVersion);
        Assert.Equal(TenantId, claimed.TenantId);
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task ClaimDue_FromAnotherTenant_CannotClaimOrReadTheJob()
    {
        var jobId = await InsertDueJobAsync(TenantId, "tenant-isolation");

        var wrongTenant = await WithTenant(
            _tenant2,
            OtherTenantId,
            () => _repository2.ClaimDueAsync("wrong-tenant-worker", TimeSpan.FromMinutes(2), 1));
        Assert.Empty(wrongTenant);

        using (_tenant1.Change(TenantId))
        {
            var visible = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                .Where(row => row.BasicId == jobId)
                .SingleAsync();
            Assert.Equal(TenantId, visible.TenantId);
            Assert.Null(visible.LeaseToken);
        }
    }

    private async Task<long> InsertDueJobAsync(long tenantId, string identity)
    {
        var jobId = SnowFlakeSingle.Instance.NextId();
        using (_tenant1.Change(tenantId))
        {
            await _db1.Insertable(new SysCrmProspectingExtractionJob
            {
                BasicId = jobId,
                TenantId = tenantId,
                RunId = SnowFlakeSingle.Instance.NextId(),
                ProviderCode = "postgres-test",
                RequestFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(identity))),
                SubmissionIdempotencyKey = Guid.NewGuid(),
                Status = CrmProspectingExtractionStatus.Pending.ToString(),
                NextPollAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                PollAttemptCount = 0,
                ResultRecordCount = 0,
                ResultFailureCount = 0,
                FencingVersion = 0
            }).ExecuteCommandAsync();
        }

        return jobId;
    }

    private static async Task<IReadOnlyList<SysCrmProspectingExtractionJob>[]> RaceAsync(
        Func<Task<IReadOnlyList<SysCrmProspectingExtractionJob>>> first,
        Func<Task<IReadOnlyList<SysCrmProspectingExtractionJob>>> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = Task.Run(async () => { await start.Task; return await first(); });
        var secondTask = Task.Run(async () => { await start.Task; return await second(); });
        start.SetResult();
        return await Task.WhenAll(firstTask, secondTask);
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
                entity => AsyncLocalCurrentTenantAccessor.Instance.Current != null &&
                    AsyncLocalCurrentTenantAccessor.Instance.Current.TenantId == entity.TenantId);
            db.Aop.DataExecuting = (_, entityInfo) =>
            {
                if (entityInfo.OperationType == DataFilterType.InsertByObject)
                    entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
            };
        });

    private static CrmProspectingExtractionJobRepository CreateRepository(ISqlSugarClient client)
    {
        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(client);
        return new CrmProspectingExtractionJobRepository(resolver.Object);
    }

    public void Dispose()
    {
        _db1.Dispose();
        _db2.Dispose();
    }
}
