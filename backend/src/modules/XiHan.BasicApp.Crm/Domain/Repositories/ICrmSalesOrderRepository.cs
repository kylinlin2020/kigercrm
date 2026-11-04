using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped repository for sales order headers.</summary>
public interface ICrmSalesOrderRepository : ISaasRepository<SysCrmSalesOrder>
{
    Task<SysCrmSalesOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
}
