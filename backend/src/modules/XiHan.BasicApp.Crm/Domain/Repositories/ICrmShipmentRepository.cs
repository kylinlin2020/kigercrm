using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped repository for shipment headers.</summary>
public interface ICrmShipmentRepository : ISaasRepository<SysCrmShipment>
{
    Task<SysCrmShipment?> GetByShipmentNumberAsync(string shipmentNumber, CancellationToken cancellationToken = default);
}
