using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped repository for quotation line snapshots.</summary>
public interface ICrmQuoteLineRepository : ISaasRepository<SysCrmQuoteLine>
{
    Task<IReadOnlyList<SysCrmQuoteLine>> ListForQuoteAsync(long quoteId, CancellationToken cancellationToken = default);
}
