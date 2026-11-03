using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped repository for shipment line quantities.</summary>
public interface ICrmShipmentLineRepository : ISaasRepository<SysCrmShipmentLine>
{
    Task<IReadOnlyList<SysCrmShipmentLine>> GetByShipmentIdAsync(long shipmentId, CancellationToken cancellationToken = default);
}
