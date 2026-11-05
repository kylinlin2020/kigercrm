using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>SqlSugar implementation; tenant and soft-delete filters come from SaasRepository.</summary>
public sealed class CrmShipmentRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmShipment>(clientResolver), ICrmShipmentRepository
{
    public async Task<SysCrmShipment?> GetByShipmentNumberAsync(string shipmentNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipmentNumber);
        cancellationToken.ThrowIfCancellationRequested();
        var matches = await CreateQueryable()
            .Where(shipment => shipment.ShipmentNumber == shipmentNumber)
            .ToListAsync(cancellationToken);
        return matches.SingleOrDefault();
    }
}
