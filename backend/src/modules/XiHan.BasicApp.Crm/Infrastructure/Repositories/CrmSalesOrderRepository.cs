using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>SqlSugar implementation; tenant and soft-delete filters come from SaasRepository.</summary>
public sealed class CrmSalesOrderRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmSalesOrder>(clientResolver), ICrmSalesOrderRepository
{
    public async Task<SysCrmSalesOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        cancellationToken.ThrowIfCancellationRequested();
        var matches = await CreateQueryable()
            .Where(order => order.OrderNumber == orderNumber)
            .ToListAsync(cancellationToken);
        return matches.SingleOrDefault();
    }
}
