using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Exceptions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Exercises the real sales-order repository's Row_Version optimistic update path against SQLite.
/// PostgreSQL-specific locking and migration behavior remain separate validation gates.
/// </summary>
public sealed class CrmSalesOrderOptimisticConcurrencySqliteIntegrationTests : IDisposable
{
    private const long TenantId = 731;
    private readonly string _connectionString = $"Data Source=kiger-crm-order-version-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();
    private readonly CrmSalesOrderRepository _repository;

    public CrmSalesOrderOptimisticConcurrencySqliteIntegrationTests()
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

        _db.CodeFirst.InitTables(typeof(SysCrmSalesOrder));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmSalesOrderRepository(resolver.Object);
    }

    [Fact]
    public async Task CreateRequestFingerprint_ShouldPersistAndRemainTenantScoped()
    {
        const string orderNumber = "SO-IDEMPOTENCY-001";
        const string fingerprintA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string fingerprintB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

        using (_tenant.Change(TenantId))
        {
            var created = await _repository.AddAsync(new SysCrmSalesOrder
            {
                OrderNumber = orderNumber,
                CreationRequestFingerprint = fingerprintA,
                CustomerId = 1001,
                CustomerNameSnapshot = "Fingerprint Tenant A",
                OrderDate = DateTime.UtcNow,
                Currency = "USD"
            });

            var replayLookup = await _repository.GetByOrderNumberAsync(orderNumber);
            Assert.NotNull(replayLookup);
            Assert.Equal(created.BasicId, replayLookup.BasicId);
            Assert.Equal(fingerprintA, replayLookup.CreationRequestFingerprint);
        }

        using (_tenant.Change(TenantId + 1))
        {
            var sameNumberOtherTenant = await _repository.AddAsync(new SysCrmSalesOrder
            {
                OrderNumber = orderNumber,
                CreationRequestFingerprint = fingerprintB,
                CustomerId = 2001,
                CustomerNameSnapshot = "Fingerprint Tenant B",
                OrderDate = DateTime.UtcNow,
                Currency = "USD"
            });

            var tenantLookup = await _repository.GetByOrderNumberAsync(orderNumber);
            Assert.NotNull(tenantLookup);
            Assert.Equal(sameNumberOtherTenant.BasicId, tenantLookup.BasicId);
            Assert.Equal(fingerprintB, tenantLookup.CreationRequestFingerprint);
        }
    }

    [Fact]
    public async Task UpdateAsync_ShouldRejectAStaleSalesOrderVersion()
    {
        using (_tenant.Change(TenantId))
        {
            var created = await _repository.AddAsync(new SysCrmSalesOrder
            {
                OrderNumber = "SO-ROW-VERSION-001",
                CustomerId = 1001,
                CustomerNameSnapshot = "Concurrency Test Customer",
                OrderDate = DateTime.UtcNow,
                Currency = "USD"
            });

            var firstRead = await _repository.GetByIdAsync(created.BasicId);
            var staleRead = await _repository.GetByIdAsync(created.BasicId);
            Assert.NotNull(firstRead);
            Assert.NotNull(staleRead);

            firstRead.FulfillmentRevision++;
            await _repository.UpdateAsync(firstRead);

            staleRead.FulfillmentRevision++;
            var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => _repository.UpdateAsync(staleRead));

            Assert.Equal("数据已被其他操作修改，请刷新后重试。", exception.Message);
            var persisted = await _repository.GetByIdAsync(created.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(1, persisted.FulfillmentRevision);
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
