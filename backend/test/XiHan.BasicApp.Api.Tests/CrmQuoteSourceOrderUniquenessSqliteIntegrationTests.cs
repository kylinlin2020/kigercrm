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
/// Exercises the actual CodeFirst source-quote unique index, including tenant and soft-delete dimensions.
/// PostgreSQL upgrade migration parity is covered separately by CrmQuoteConversionMigrationTests.
/// </summary>
public sealed class CrmQuoteSourceOrderUniquenessSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 7731;
    private readonly string _connectionString = $"Data Source=kiger-crm-quote-source-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    // Repository write guards read AsyncLocalCurrentTenantAccessor.Instance.Current;
    // use the framework CurrentTenant so filters, AOP auditing and the guard agree.
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly CrmSalesOrderRepository _repository;

    public CrmQuoteSourceOrderUniquenessSqliteIntegrationTests()
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

        _db.CodeFirst.InitTables(typeof(SysCrmSalesOrder));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmSalesOrderRepository(resolver.Object);
    }

    [Fact]
    public async Task SourceQuoteIndex_ShouldBeUniquePerTenantAndSoftDeleteState()
    {
        using (_tenant.Change(TenantId))
        {
            await _repository.AddAsync(CreateOrder("SO-QUOTE-001", 44001, isDeleted: false));
            // Insert-time audit forces IsDeleted=false. Insert under a distinct source key,
            // then move it to the deleted state of the key under test so the index's
            // IsDeleted dimension is exercised rather than the audit hook.
            var softDeleted = await _repository.AddAsync(CreateOrder("SO-QUOTE-002", 44002, isDeleted: false));
            await _db.Updateable<SysCrmSalesOrder>()
                .SetColumns(order => order.IsDeleted, true)
                .SetColumns(order => order.SourceQuoteId, 44001)
                .Where(order => order.BasicId == softDeleted.BasicId)
                .ExecuteCommandAsync();

            await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(
                () => _repository.AddAsync(CreateOrder("SO-QUOTE-003", 44001, isDeleted: false)));
        }

        using (_tenant.Change(TenantId + 1))
        {
            var otherTenantOrder = await _repository.AddAsync(
                CreateOrder("SO-QUOTE-004", 44001, isDeleted: false));

            Assert.Equal(44001, otherTenantOrder.SourceQuoteId);
            Assert.Equal(TenantId + 1, otherTenantOrder.TenantId);
        }
    }

    private static SysCrmSalesOrder CreateOrder(string number, long sourceQuoteId, bool isDeleted)
        => new()
        {
            OrderNumber = number,
            SourceQuoteId = sourceQuoteId,
            CustomerId = 1001,
            CustomerNameSnapshot = "Quote Index Integration Customer",
            OrderDate = DateTime.UtcNow,
            Currency = "USD",
            IsDeleted = isDeleted
        };

    public void Dispose()
    {
        _tenantScope.Dispose();
        AsyncLocalCurrentTenantAccessor.Instance.Current = null;
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static long ScopeId() => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;

}
