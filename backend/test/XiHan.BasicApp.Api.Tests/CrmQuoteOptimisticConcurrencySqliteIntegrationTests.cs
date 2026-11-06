using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Exceptions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Exercises the real quote repository's Row_Version optimistic update path against SQLite.
/// This verifies stale quote writes are rejected, but does not simulate the full quote-to-order
/// UnitOfWork or replace PostgreSQL two-connection conversion/rollback tests.
/// </summary>
public sealed class CrmQuoteOptimisticConcurrencySqliteIntegrationTests : IDisposable
{
    private const long TenantId = 7732;
    private readonly string _connectionString = $"Data Source=kiger-crm-quote-version-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();
    private readonly CrmQuoteRepository _repository;

    public CrmQuoteOptimisticConcurrencySqliteIntegrationTests()
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

        _db.CodeFirst.InitTables(typeof(SysCrmQuote));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmQuoteRepository(resolver.Object);
    }

    [Fact]
    public async Task UpdateAsync_ShouldRejectAStaleAcceptedQuoteConversionLink()
    {
        using (_tenant.Change(TenantId))
        {
            var created = await _repository.AddAsync(new SysCrmQuote
            {
                QuoteNumber = "QT-ROW-VERSION-001",
                Version = 1,
                CustomerId = 1001,
                CustomerNameSnapshot = "Quote Concurrency Test Customer",
                Status = CrmQuoteStatus.Accepted,
                IssueDate = DateTime.UtcNow.Date,
                Currency = "USD",
                GrandTotal = 125m
            });

            var firstRead = await _repository.GetByIdAsync(created.BasicId);
            var staleRead = await _repository.GetByIdAsync(created.BasicId);
            Assert.NotNull(firstRead);
            Assert.NotNull(staleRead);

            firstRead.Status = CrmQuoteStatus.Converted;
            firstRead.ConvertedOrderId = 88001;
            await _repository.UpdateAsync(firstRead);

            staleRead.Status = CrmQuoteStatus.Converted;
            staleRead.ConvertedOrderId = 88002;
            var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => _repository.UpdateAsync(staleRead));

            Assert.Equal("数据已被其他操作修改，请刷新后重试。", exception.Message);
            var persisted = await _repository.GetByIdAsync(created.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmQuoteStatus.Converted, persisted.Status);
            Assert.Equal(88001, persisted.ConvertedOrderId);
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
