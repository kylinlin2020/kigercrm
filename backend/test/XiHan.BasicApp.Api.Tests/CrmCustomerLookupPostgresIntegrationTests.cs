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

/// <summary>PostgreSQL integration coverage for tenant-scoped, bounded CRM customer lookup.</summary>
public sealed class CrmCustomerLookupPostgresIntegrationTests : IDisposable
{
    private const long TenantId = 98741;
    private const long OtherTenantId = 98742;
    private readonly ICurrentTenant _tenant1 = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly ICurrentTenant _tenant2 = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly SqlSugarScope _db1;
    private readonly CrmCustomerLookupRepository _repository;

    public static bool PostgresConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING"));

    public CrmCustomerLookupPostgresIntegrationTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("PostgreSQL integration tests require KIGER_TEST_POSTGRES_CONNECTION_STRING.");

        _db1 = CreateClient(connectionString);
        _db1.CodeFirst.InitTables(typeof(SysCrmCustomer));
        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db1);
        _repository = new CrmCustomerLookupRepository(resolver.Object);
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task GetExistingIdsAsync_ReturnsOnlyCustomersOwnedByCurrentTenant()
    {
        var ownId = await InsertCustomerAsync(TenantId, "tenant-owned");
        var foreignId = await InsertCustomerAsync(OtherTenantId, "foreign-tenant");
        try
        {
            var result = await WithTenant(_tenant1, TenantId, () =>
                _repository.GetExistingIdsAsync(new[] { ownId, foreignId, ownId }));

            Assert.Contains(ownId, result);
            Assert.DoesNotContain(foreignId, result);
            Assert.Single(result);
        }
        finally
        {
            await DeleteCustomersAsync(ownId, foreignId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task GetExistingIdsAsync_ChunksDistinctIdsBeyondSingleQueryLimit()
    {
        var ids = new List<long>();
        try
        {
            using (_tenant1.Change(TenantId))
            {
                for (var offset = 0; offset < 501; offset += 100)
                {
                    var batch = Enumerable.Range(offset, Math.Min(100, 501 - offset))
                        .Select(index => new SysCrmCustomer
                        {
                            BasicId = SnowFlakeSingle.Instance.NextId(),
                            TenantId = TenantId,
                            Name = $"lookup-batch-{Guid.NewGuid():N}-{index}"
                        })
                        .ToArray();
                    await _db1.Insertable(batch).ExecuteCommandAsync();
                    ids.AddRange(batch.Select(customer => customer.BasicId));
                }
            }

            var requested = ids.Concat(ids.Take(20)).ToArray();
            var result = await WithTenant(_tenant1, TenantId, () =>
                _repository.GetExistingIdsAsync(requested));

            Assert.Equal(501, result.Count);
            Assert.All(ids, id => Assert.Contains(id, result));
        }
        finally
        {
            await DeleteCustomersAsync(ids.ToArray());
        }
    }

    private async Task<long> InsertCustomerAsync(long tenantId, string label)
    {
        var id = SnowFlakeSingle.Instance.NextId();
        using (_tenant1.Change(tenantId))
        {
            await _db1.Insertable(new SysCrmCustomer
            {
                BasicId = id,
                TenantId = tenantId,
                Name = $"lookup-{label}-{Guid.NewGuid():N}"
            }).ExecuteCommandAsync();
        }

        return id;
    }

    private async Task DeleteCustomersAsync(params long[] ids)
    {
        if (ids.Length == 0) return;
        using (_tenant1.Change(TenantId))
        {
            await _db1.Deleteable<SysCrmCustomer>()
                .Where(customer => ids.Contains(customer.BasicId))
                .ExecuteCommandAsync();
        }
        using (_tenant2.Change(OtherTenantId))
        {
            await _db1.Deleteable<SysCrmCustomer>()
                .Where(customer => ids.Contains(customer.BasicId))
                .ExecuteCommandAsync();
        }
    }

    private static async Task<T> WithTenant<T>(ICurrentTenant tenant, long tenantId, Func<Task<T>> action)
    {
        using (tenant.Change(tenantId))
            return await action();
    }

    private static SqlSugarScope CreateClient(string connectionString)
        => new(new ConnectionConfig
        {
            DbType = DbType.PostgreSQL,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        }, db =>
        {
            db.QueryFilter.AddTableFilter<ISoftDelete>(entity => !entity.IsDeleted);
            db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(
                entity => ScopeTenantId() == entity.TenantId);
            db.Aop.DataExecuting = (_, entityInfo) =>
            {
                if (entityInfo.OperationType == DataFilterType.InsertByObject)
                {
                    entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                    entityInfo.ToCreated(EntityAuditContext.From(null, ScopeTenantId()));
                }
            };
        });

    private static long ScopeTenantId()
        => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;

    public void Dispose() => _db1.Dispose();
}
