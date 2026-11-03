using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>SqlSugar implementation; tenant and soft-delete filters come from SaasRepository.</summary>
public sealed class CrmSalesOrderLineRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmSalesOrderLine>(clientResolver), ICrmSalesOrderLineRepository {

    public async Task<IReadOnlyList<SysCrmSalesOrderLine>> ListForOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId));
        cancellationToken.ThrowIfCancellationRequested();
        return await CreateQueryable()
            .Where(line => line.SalesOrderId == orderId)
            .OrderBy(line => line.SortOrder)
            .ThenBy(line => line.BasicId)
            .ToListAsync(cancellationToken);
    }
}
