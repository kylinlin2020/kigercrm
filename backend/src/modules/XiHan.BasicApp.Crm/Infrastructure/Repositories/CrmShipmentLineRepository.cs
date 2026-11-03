using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>SqlSugar implementation; tenant and soft-delete filters come from SaasRepository.</summary>
public sealed class CrmShipmentLineRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmShipmentLine>(clientResolver), ICrmShipmentLineRepository
{
    public async Task<IReadOnlyList<SysCrmShipmentLine>> GetByShipmentIdAsync(long shipmentId, CancellationToken cancellationToken = default)
    {
        if (shipmentId <= 0) throw new ArgumentOutOfRangeException(nameof(shipmentId));
        cancellationToken.ThrowIfCancellationRequested();
        return await CreateQueryable()
            .Where(line => line.ShipmentId == shipmentId)
            .OrderBy(line => line.BasicId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyForSalesOrderLineAsync(long salesOrderLineId, CancellationToken cancellationToken = default)
    {
        if (salesOrderLineId <= 0) throw new ArgumentOutOfRangeException(nameof(salesOrderLineId));
        cancellationToken.ThrowIfCancellationRequested();
        return await CreateQueryable().AnyAsync(line => line.SalesOrderLineId == salesOrderLineId, cancellationToken);
    }
}
