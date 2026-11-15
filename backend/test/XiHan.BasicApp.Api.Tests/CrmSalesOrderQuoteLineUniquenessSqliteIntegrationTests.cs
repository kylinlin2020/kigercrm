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
/// Exercises the actual CodeFirst unique index protecting copied quotation-line identity.
/// This verifies the database-level duplicate guard independently of application wiring tests.
/// </summary>
public sealed class CrmSalesOrderQuoteLineUniquenessSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 8841;
    private readonly string _connectionString = $"Data Source=kiger-crm-order-quote-line-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    // Repository write guards read AsyncLocalCurrentTenantAccessor.Instance.Current;
    // use the framework CurrentTenant so filters, AOP auditing and the guard agree.
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly CrmSalesOrderLineRepository _repository;

    public CrmSalesOrderQuoteLineUniquenessSqliteIntegrationTests()
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

        _db.CodeFirst.InitTables(typeof(SysCrmSalesOrderLine));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmSalesOrderLineRepository(resolver.Object);
    }

    [Fact]
    public async Task SourceQuoteLineIndex_ShouldPreventDuplicateCopiesAndRespectTenantAndSoftDeleteKeys()
    {
        using (_tenant.Change(TenantId))
        {
            await _repository.AddAsync(CreateLine(orderId: 55001, quoteLineId: 66001, isDeleted: false));
            await _repository.AddAsync(CreateLine(orderId: 55001, quoteLineId: 66001, isDeleted: true));

            await Assert.ThrowsAsync<SqlSugarException>(
                () => _repository.AddAsync(CreateLine(orderId: 55001, quoteLineId: 66001, isDeleted: false)));

            await _repository.AddAsync(CreateLine(orderId: 55001, quoteLineId: 66002, isDeleted: false));
            await _repository.AddAsync(CreateLine(orderId: 55002, quoteLineId: 66001, isDeleted: false));
        }

        using (_tenant.Change(TenantId + 1))
        {
            var otherTenantLine = await _repository.AddAsync(
                CreateLine(orderId: 55001, quoteLineId: 66001, isDeleted: false));

            Assert.Equal(66001, otherTenantLine.SourceQuoteLineId);
            Assert.Equal(TenantId + 1, otherTenantLine.TenantId);
        }
    }

    private static SysCrmSalesOrderLine CreateLine(long orderId, long quoteLineId, bool isDeleted)
        => new()
        {
            SalesOrderId = orderId,
            SourceQuoteLineId = quoteLineId,
            Description = "Quoted product snapshot",
            Quantity = 1,
            UnitPrice = 25,
            DiscountRate = 0,
            TaxRate = 0,
            LineSubtotal = 25,
            LineDiscount = 0,
            LineTax = 0,
            LineTotal = 25,
            SortOrder = 1,
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
