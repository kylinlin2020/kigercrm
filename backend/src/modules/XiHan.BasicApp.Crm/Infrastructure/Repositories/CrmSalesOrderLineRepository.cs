using System.Linq.Expressions;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Repository;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>SqlSugar implementation with tenant filters and audited soft deletion.</summary>
public sealed class CrmSalesOrderLineRepository(ISqlSugarClientResolver clientResolver)
    : SqlSugarSoftDeleteRepository<SysCrmSalesOrderLine, long>(clientResolver), ICrmSalesOrderLineRepository
{
    public async Task<bool> AnyIgnoreTenantAsync(Expression<Func<SysCrmSalesOrderLine, bool>> predicate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        cancellationToken.ThrowIfCancellationRequested();
        return await CreateNoTenantQueryable().Where(predicate).AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SysCrmSalesOrderLine>> ListForOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId));
        cancellationToken.ThrowIfCancellationRequested();
        return await CreateQueryable()
            .Where(line => line.SalesOrderId == orderId)
            .OrderBy(line => line.SortOrder)
            .OrderBy(line => line.BasicId)
            .ToListAsync(cancellationToken);
    }
}
