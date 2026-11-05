using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Verifies the actual tenant-scoped unique index for optional initial-score idempotency keys.
/// SQLite validates the index shape; PostgreSQL independent-connection race semantics remain a separate gate.
/// </summary>
public sealed class CrmProspectingScoreIdempotencySqliteIntegrationTests : IDisposable
{
    private const long TenantId = 9851;
    private readonly string _connectionString = $"Data Source=kiger-crm-score-idempotency-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();
    private readonly CrmProspectingScoreRepository _repository;

    public CrmProspectingScoreIdempotencySqliteIntegrationTests()
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

        _db.CodeFirst.InitTables(typeof(SysCrmProspectingScore));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmProspectingScoreRepository(resolver.Object);
    }

    [Fact]
    public async Task IdempotencyKey_ShouldBeUniqueWithinTenantAndNullableForHistoricalScores()
    {
        long firstScoreId;
        using (_tenant.Change(TenantId))
        {
            var first = await _repository.AddAsync(CreateScore("initial:100:v1"));
            firstScoreId = first.BasicId;

            Assert.Equal(firstScoreId, (await _repository.GetByIdempotencyKeyAsync("initial:100:v1"))?.BasicId);
            await Assert.ThrowsAsync<SqlSugarException>(() => _repository.AddAsync(CreateScore("initial:100:v1")));

            // Historical/re-evaluated snapshots intentionally omit the one-time key.
            await _repository.AddAsync(CreateScore(null));
            await _repository.AddAsync(CreateScore(null));
        }

        using (_tenant.Change(TenantId + 1))
        {
            Assert.Null(await _repository.GetByIdempotencyKeyAsync("initial:100:v1"));
            var otherTenant = await _repository.AddAsync(CreateScore("initial:100:v1"));
            Assert.NotEqual(firstScoreId, otherTenant.BasicId);
            Assert.Equal(otherTenant.BasicId, (await _repository.GetByIdempotencyKeyAsync("initial:100:v1"))?.BasicId);
        }

        using (_tenant.Change(TenantId))
            Assert.Equal(firstScoreId, (await _repository.GetByIdempotencyKeyAsync("initial:100:v1"))?.BasicId);
    }

    private static SysCrmProspectingScore CreateScore(string? idempotencyKey) => new()
    {
        CandidateId = 100,
        IdempotencyKey = idempotencyKey,
        ScorecardVersion = "v1",
        Total = 0,
        Priority = 0,
        IdentityPoints = 0,
        ProcurementPoints = 0,
        ProductFitPoints = 0,
        MarketFitPoints = 0,
        RecencyPoints = 0,
        EvidencePoints = 0,
        ContactPoints = 0,
        ReviewGatesJson = "[]",
        EvaluatedAtUtc = DateTimeOffset.UtcNow,
        EvidenceReferencesJson = "[]"
    };

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
