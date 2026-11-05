using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;
using XiHan.Framework.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped, audited soft-delete repository for sales order line snapshots.</summary>
public interface ICrmSalesOrderLineRepository :
    ISaasRepository<SysCrmSalesOrderLine>,
    ISoftDeleteRepositoryBase<SysCrmSalesOrderLine, long>
{
    Task<IReadOnlyList<SysCrmSalesOrderLine>> ListForOrderAsync(long orderId, CancellationToken cancellationToken = default);
}
