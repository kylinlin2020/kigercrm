using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Strictly tenant-scoped immutable strategy-version repository.</summary>
public interface ICrmProspectingStrategyRepository : ISaasRepository<SysCrmProspectingStrategy>
{
    /// <summary>Read the newest strategy versions for one campaign with the result limit applied in SQL.</summary>
    Task<IReadOnlyList<SysCrmProspectingStrategy>> GetLatestByCampaignAsync(long campaignId, int limit, CancellationToken cancellationToken = default);
}
