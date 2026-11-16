using Microsoft.Data.Sqlite;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Exercises the tenant-scoped unique ConvertedLeadId index with the real SqlSugar CodeFirst mapping.
/// PostgreSQL locking and two-connection race behavior remain separate validation gates.
/// </summary>
public sealed class CrmLeadConversionCustomerUniquenessSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 8831;
    private readonly string _connectionString = $"Data Source=kiger-crm-customer-conversion-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    // Repository write guards read AsyncLocalCurrentTenantAccessor.Instance.Current;
    // use the framework CurrentTenant so filters, AOP auditing and the guard agree.
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;

    public CrmLeadConversionCustomerUniquenessSqliteIntegrationTests()
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

        _db.CodeFirst.InitTables(typeof(SysCrmCustomer));
    }

    [Fact]
    public async Task ConvertedLeadId_ShouldBeUniquePerTenant_AndNullableForOrdinaryCustomers()
    {
        await InsertCustomerAsync(TenantId, 99101, "Converted customer");
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(
            () => InsertCustomerAsync(TenantId, 99101, "Concurrent duplicate"));

        // Multiple ordinary customers without a source Lead remain valid.
        await InsertCustomerAsync(TenantId, null, "Ordinary customer one");
        await InsertCustomerAsync(TenantId, null, "Ordinary customer two");

        // The same Lead key in another tenant is independent.
        await InsertCustomerAsync(TenantId + 1, 99101, "Other tenant customer");
    }

    private async Task InsertCustomerAsync(long tenantId, long? leadId, string name)
    {
        using (_tenant.Change(tenantId))
        {
            await _db.Insertable(new SysCrmCustomer
            {
                Name = name,
                ConvertedLeadId = leadId
            }).ExecuteCommandAsync();
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
