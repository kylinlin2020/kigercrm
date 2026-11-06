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
/// PostgreSQL-backed two-client tests for the real conditional-claim repositories.
/// These tests do not replace the higher-level DI/UoW application-service race tests.
/// </summary>
public sealed class CrmConversionClaimPostgresIntegrationTests : IDisposable
{
    private const long TenantId = 98621;
    private readonly ProbeCurrentTenant _tenant1 = new();
    private readonly ProbeCurrentTenant _tenant2 = new();
    private readonly SqlSugarScope _db1;
    private readonly SqlSugarScope _db2;
    private readonly CrmLeadRepository _leadRepository1;
    private readonly CrmLeadRepository _leadRepository2;
    private readonly CrmProspectingCandidateRepository _candidateRepository1;
    private readonly CrmProspectingCandidateRepository _candidateRepository2;

    public static bool PostgresConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING"));

    public CrmConversionClaimPostgresIntegrationTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("PostgreSQL integration tests require KIGER_TEST_POSTGRES_CONNECTION_STRING.");

        _db1 = CreateClient(connectionString, _tenant1);
        _db2 = CreateClient(connectionString, _tenant2);

        _db1.CodeFirst.InitTables(typeof(SysCrmLead), typeof(SysCrmProspectingCandidate));

        _leadRepository1 = CreateLeadRepository(_db1);
        _leadRepository2 = CreateLeadRepository(_db2);
        _candidateRepository1 = CreateCandidateRepository(_db1);
        _candidateRepository2 = CreateCandidateRepository(_db2);
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task LeadClaim_TwoIndependentClients_ExactlyOneWins()
    {
        var leadId = SnowFlakeSingle.Instance.NextId();
        using (_tenant1.Change(TenantId))
        {
            await _db1.Insertable(new SysCrmLead
            {
                BasicId = leadId,
                TenantId = TenantId,
                CompanyName = $"Postgres lead {leadId}",
                Status = CrmLeadStatus.New
            }).ExecuteCommandAsync();
        }

        var results = await RaceAsync(
            () => WithTenant(_tenant1, () => _leadRepository1.TryClaimForConversionAsync(leadId)),
            () => WithTenant(_tenant2, () => _leadRepository2.TryClaimForConversionAsync(leadId)));

        Assert.Equal(1, results.Count(value => value));
        Assert.Equal(1, results.Count(value => !value));

        using (_tenant1.Change(TenantId))
        {
            var persisted = await _db1.Queryable<SysCrmLead>().Where(row => row.BasicId == leadId).SingleAsync();
            Assert.Equal(CrmLeadStatus.Converting, persisted.Status);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task CandidateClaim_TwoIndependentClients_ExactlyOneWins()
    {
        var candidateId = SnowFlakeSingle.Instance.NextId();
        using (_tenant1.Change(TenantId))
        {
            await _db1.Insertable(new SysCrmProspectingCandidate
            {
                BasicId = candidateId,
                TenantId = TenantId,
                RunId = 81001,
                ProviderCode = "postgres-test",
                ExternalId = $"candidate-{candidateId}",
                CompanyName = $"Postgres candidate {candidateId}",
                Status = CrmProspectingCandidateStatus.Approved
            }).ExecuteCommandAsync();
        }

        var results = await RaceAsync(
            () => WithTenant(_tenant1, () => _candidateRepository1.TryClaimForConversionAsync(candidateId)),
            () => WithTenant(_tenant2, () => _candidateRepository2.TryClaimForConversionAsync(candidateId)));

        Assert.Equal(1, results.Count(value => value));
        Assert.Equal(1, results.Count(value => !value));

        using (_tenant1.Change(TenantId))
        {
            var persisted = await _db1.Queryable<SysCrmProspectingCandidate>()
                .Where(row => row.BasicId == candidateId)
                .SingleAsync();
            Assert.Equal(CrmProspectingCandidateStatus.Converting, persisted.Status);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task LeadClaim_FromAnotherTenant_CannotClaimOrReadTheLead()
    {
        var leadId = SnowFlakeSingle.Instance.NextId();
        using (_tenant1.Change(TenantId))
        {
            await _db1.Insertable(new SysCrmLead
            {
                BasicId = leadId,
                TenantId = TenantId,
                CompanyName = $"Tenant isolation lead {leadId}",
                Status = CrmLeadStatus.New
            }).ExecuteCommandAsync();
        }

        using (_tenant2.Change(TenantId + 1))
        {
            Assert.False(await _leadRepository2.TryClaimForConversionAsync(leadId));
            Assert.Null(await _db2.Queryable<SysCrmLead>().Where(row => row.BasicId == leadId).SingleAsync());
        }

        using (_tenant1.Change(TenantId))
        {
            var persisted = await _db1.Queryable<SysCrmLead>().Where(row => row.BasicId == leadId).SingleAsync();
            Assert.Equal(CrmLeadStatus.New, persisted.Status);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task CandidateClaim_FromAnotherTenant_CannotClaimOrReadTheCandidate()
    {
        var candidateId = SnowFlakeSingle.Instance.NextId();
        using (_tenant1.Change(TenantId))
        {
            await _db1.Insertable(new SysCrmProspectingCandidate
            {
                BasicId = candidateId,
                TenantId = TenantId,
                RunId = 81002,
                ProviderCode = "postgres-tenant-test",
                ExternalId = $"candidate-{candidateId}",
                CompanyName = $"Tenant isolation candidate {candidateId}",
                Status = CrmProspectingCandidateStatus.Approved
            }).ExecuteCommandAsync();
        }

        using (_tenant2.Change(TenantId + 1))
        {
            Assert.False(await _candidateRepository2.TryClaimForConversionAsync(candidateId));
            Assert.Null(await _db2.Queryable<SysCrmProspectingCandidate>()
                .Where(row => row.BasicId == candidateId)
                .SingleAsync());
        }

        using (_tenant1.Change(TenantId))
        {
            var persisted = await _db1.Queryable<SysCrmProspectingCandidate>()
                .Where(row => row.BasicId == candidateId)
                .SingleAsync();
            Assert.Equal(CrmProspectingCandidateStatus.Approved, persisted.Status);
        }
    }

    private static async Task<bool[]> RaceAsync(Func<Task<bool>> first, Func<Task<bool>> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = Task.Run(async () => { await start.Task; return await first(); });
        var secondTask = Task.Run(async () => { await start.Task; return await second(); });
        start.SetResult();
        return await Task.WhenAll(firstTask, secondTask);
    }

    private static async Task<T> WithTenant<T>(ProbeCurrentTenant tenant, Func<Task<T>> action)
    {
        using (tenant.Change(TenantId))
            return await action();
    }

    private static SqlSugarScope CreateClient(string connectionString, ProbeCurrentTenant tenant)
        => new(new ConnectionConfig
        {
            DbType = DbType.PostgreSQL,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        }, db =>
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

    private static CrmLeadRepository CreateLeadRepository(ISqlSugarClient client)
    {
        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(client);
        return new CrmLeadRepository(resolver.Object);
    }

    private static CrmProspectingCandidateRepository CreateCandidateRepository(ISqlSugarClient client)
    {
        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(client);
        return new CrmProspectingCandidateRepository(resolver.Object);
    }

    private static long ScopeId(ProbeCurrentTenant tenant) => tenant.Id ?? 0;

    public void Dispose()
    {
        _db1.Dispose();
        _db2.Dispose();
    }

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
