using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Exercises entity mapping and tenant-scoped reads for durable provider execution rows.
/// PostgreSQL row-lock, lease-fencing and concurrent materialization guarantees require the separate
/// PostgreSQL acceptance gate and are intentionally not inferred from this SQLite test.
/// </summary>
public sealed class CrmProspectingRunProviderExecutionSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 98431;
    private readonly string _connectionString = $"Data Source=kiger-provider-execution-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();
    private readonly CrmProspectingRunProviderExecutionRepository _repository;

    public CrmProspectingRunProviderExecutionSqliteIntegrationTests()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        var tenant = _tenant;
        _db = new SqlSugarScope(new ConnectionConfig
        {
            DbType = DbType.Sqlite,
            ConnectionString = _connectionString,
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
        _db.CodeFirst.InitTables(typeof(SysCrmProspectingRunProviderExecution));
        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmProspectingRunProviderExecutionRepository(resolver.Object);
    }

    [Fact]
    public async Task ProviderExecution_ShouldRoundTripCheckpointMetadataAndRemainTenantScoped()
    {
        long rowId;
        using (_tenant.Change(TenantId))
        {
            var row = await _repository.AddAsync(new SysCrmProspectingRunProviderExecution
            {
                TenantId = TenantId,
                RunId = 7123,
                PlanVersion = 4,
                PlanOrdinal = 2,
                ProviderCode = "sam.gov",
                RequestFingerprint = new string('A', 64),
                State = "Checkpointed",
                AttemptCount = 1,
                CheckpointJson = "{\"records\":[]}",
                CheckpointHash = new string('B', 64),
                CheckpointBytes = 14,
                RawRecordCount = 0,
                ProjectedRecordCount = 0,
                FailureCount = 0
            });
            rowId = row.BasicId;

            var rows = await _repository.GetByRunAsync(7123);
            var persisted = Assert.Single(rows);
            Assert.Equal(rowId, persisted.BasicId);
            Assert.Equal(4, persisted.PlanVersion);
            Assert.Equal(2, persisted.PlanOrdinal);
            Assert.Equal("sam.gov", persisted.ProviderCode);
            Assert.Equal("Checkpointed", persisted.State);
            Assert.Equal("{\"records\":[]}", persisted.CheckpointJson);
            Assert.Equal(new string('B', 64), persisted.CheckpointHash);
            Assert.Equal(14, persisted.CheckpointBytes);
        }

        using (_tenant.Change(TenantId + 1))
            Assert.Empty(await _repository.GetByRunAsync(7123));
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

    public void Dispose()
    {
        _db.Dispose();
        _keepAlive.Dispose();
    }
}
