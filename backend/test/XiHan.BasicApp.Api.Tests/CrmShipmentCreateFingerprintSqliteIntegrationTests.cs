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
/// Verifies that the shipment create fingerprint is persisted and the replay lookup
/// respects strict tenant filtering. PostgreSQL concurrency remains a separate gate.
/// </summary>
public sealed class CrmShipmentCreateFingerprintSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 841;
    private readonly string _connectionString = $"Data Source=kiger-crm-shipment-fingerprint-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    // Repository write guards read AsyncLocalCurrentTenantAccessor.Instance.Current;
    // use the framework CurrentTenant so filters, AOP auditing and the guard agree.
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly CrmShipmentRepository _repository;

    public CrmShipmentCreateFingerprintSqliteIntegrationTests()
    {
        _tenantScope = _tenant.Change(TenantId);
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
                db.QueryFilter.AddTableFilter<IMultiTenantEntity>(entity => entity.TenantId == 0 || ScopeId() == entity.TenantId);
                db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(entity => ScopeId() == entity.TenantId);
                db.Aop.DataExecuting = (_, entityInfo) =>
                {
                    if (entityInfo.OperationType == DataFilterType.InsertByObject)
                    {
                        entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                        entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                    }
                };
            });

        _db.CodeFirst.InitTables(typeof(SysCrmShipment));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmShipmentRepository(resolver.Object);
    }

    [Fact]
    public async Task CreateRequestFingerprint_ShouldPersistAndRemainTenantScoped()
    {
        const string shipmentNumber = "SH-IDEMPOTENCY-001";
        const string fingerprintA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string fingerprintB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

        using (_tenant.Change(TenantId))
        {
            var created = await _repository.AddAsync(new SysCrmShipment
            {
                ShipmentNumber = shipmentNumber,
                CreationRequestFingerprint = fingerprintA,
                SalesOrderId = 9001
            });

            var replayLookup = await _repository.GetByShipmentNumberAsync(shipmentNumber);
            Assert.NotNull(replayLookup);
            Assert.Equal(created.BasicId, replayLookup.BasicId);
            Assert.Equal(fingerprintA, replayLookup.CreationRequestFingerprint);
        }

        using (_tenant.Change(TenantId + 1))
        {
            var sameNumberOtherTenant = await _repository.AddAsync(new SysCrmShipment
            {
                ShipmentNumber = shipmentNumber,
                CreationRequestFingerprint = fingerprintB,
                SalesOrderId = 9002
            });

            var tenantLookup = await _repository.GetByShipmentNumberAsync(shipmentNumber);
            Assert.NotNull(tenantLookup);
            Assert.Equal(sameNumberOtherTenant.BasicId, tenantLookup.BasicId);
            Assert.Equal(fingerprintB, tenantLookup.CreationRequestFingerprint);
        }
    }

    public void Dispose()
    {
        _tenantScope.Dispose();
        AsyncLocalCurrentTenantAccessor.Instance.Current = null;
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static long ScopeId() => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;

}
