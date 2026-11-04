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
/// Verifies CodeFirst persistence, permanent uniqueness, and strict tenant isolation
/// for the CRM document creation ledger. PostgreSQL transaction/concurrency tests remain separate.
/// </summary>
public sealed class CrmCreateRequestLedgerSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 9841;
    private readonly string _connectionString = $"Data Source=kiger-crm-create-request-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();
    private readonly CrmCreateRequestRepository _repository;

    public CrmCreateRequestLedgerSqliteIntegrationTests()
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

        _db.CodeFirst.InitTables(typeof(SysCrmCreateRequest));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmCreateRequestRepository(resolver.Object);
    }

    [Fact]
    public async Task RequestKey_ShouldPersistAndRemainStrictlyTenantScoped()
    {
        const string requestKey = "client-create-request-001";
        const string payloadA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string payloadB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

        using (_tenant.Change(TenantId))
        {
            var created = await _repository.AddAsync(new SysCrmCreateRequest
            {
                DocumentType = "SALES_ORDER",
                RequestKey = requestKey,
                PayloadHash = payloadA,
                DocumentId = 10001,
                DocumentNumber = "SO-10001"
            });

            var found = await _repository.GetByRequestKeyAsync("SALES_ORDER", requestKey);
            Assert.NotNull(found);
            Assert.Equal(created.BasicId, found.BasicId);
            Assert.Equal(payloadA, found.PayloadHash);
            Assert.Equal(10001, found.DocumentId);
        }

        using (_tenant.Change(TenantId + 1))
        {
            Assert.Null(await _repository.GetByRequestKeyAsync("SALES_ORDER", requestKey));
            var otherTenant = await _repository.AddAsync(new SysCrmCreateRequest
            {
                DocumentType = "SALES_ORDER",
                RequestKey = requestKey,
                PayloadHash = payloadB,
                DocumentId = 10002,
                DocumentNumber = "SO-10002"
            });
            Assert.NotEqual(0, otherTenant.BasicId);
            Assert.NotNull(await _repository.GetByRequestKeyAsync("SALES_ORDER", requestKey));
        }
    }

    [Fact]
    public async Task RequestKey_ShouldBeUniqueWithinTenantAndDocumentType()
    {
        using (_tenant.Change(TenantId))
        {
            _ = await _repository.AddAsync(new SysCrmCreateRequest
            {
                DocumentType = "SALES_ORDER",
                RequestKey = "same-key",
                PayloadHash = new string('A', 64)
            });

            await Assert.ThrowsAnyAsync<Exception>(() => _repository.AddAsync(new SysCrmCreateRequest
            {
                DocumentType = "SALES_ORDER",
                RequestKey = "same-key",
                PayloadHash = new string('B', 64)
            }));

            // A separate document type has an independent key namespace.
            _ = await _repository.AddAsync(new SysCrmCreateRequest
            {
                DocumentType = "SHIPMENT",
                RequestKey = "same-key",
                PayloadHash = new string('C', 64)
            });
        }
    }

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
