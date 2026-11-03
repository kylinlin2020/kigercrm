using Microsoft.Data.Sqlite;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Verifies the CodeFirst unique-slot index on SQLite, including NULL and tenant semantics.</summary>
public sealed class CrmPrimaryContactSqliteIntegrationTests : IDisposable
{
    private const long TenantA = 73;
    private const long TenantB = 74;

    private readonly string _connectionString = $"Data Source=kiger-crm-primary-contact-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();

    public CrmPrimaryContactSqliteIntegrationTests()
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
                db.Aop.DataExecuting = (_, entityInfo) =>
                {
                    if (entityInfo.OperationType == DataFilterType.InsertByObject)
                    {
                        entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                        entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                    }
                };
            });

        _db.CodeFirst.InitTables(typeof(SysCrmContact));
    }

    [Fact]
    public async Task UniquePrimarySlot_ShouldAllowManyNonPrimaryButOnlyOnePrimaryPerTenantCustomer()
    {
        using (_tenant.Change(TenantA))
        {
            await InsertAsync(100, isPrimary: true);
            await InsertAsync(100, isPrimary: false);
            await InsertAsync(100, isPrimary: false);
            await Assert.ThrowsAnyAsync<Exception>(() => InsertAsync(100, isPrimary: true));
            await InsertAsync(101, isPrimary: true);
        }

        using (_tenant.Change(TenantB))
        {
            await InsertAsync(100, isPrimary: true);
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private async Task InsertAsync(long customerId, bool isPrimary)
    {
        var contact = new SysCrmContact
        {
            CustomerId = customerId,
            FirstName = "Test",
            LastName = "Contact",
            IsPrimary = isPrimary,
            PrimaryCustomerId = isPrimary ? customerId : null
        };
        _ = await _db.Insertable(contact).ExecuteCommandAsync();
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
