using Microsoft.Data.Sqlite;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
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
    private readonly ProbeCurrentTenant _tenant = new();

    public CrmLeadConversionCustomerUniquenessSqliteIntegrationTests()
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

        _db.CodeFirst.InitTables(typeof(SysCrmCustomer));
    }

    [Fact]
    public async Task ConvertedLeadId_ShouldBeUniquePerTenant_AndNullableForOrdinaryCustomers()
    {
        await InsertCustomerAsync(TenantId, 99101, "Converted customer");
        await Assert.ThrowsAsync<SqlSugarException>(
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
