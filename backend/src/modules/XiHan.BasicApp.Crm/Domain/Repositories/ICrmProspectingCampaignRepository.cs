using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Strictly tenant-scoped campaign repository.</summary>
public interface ICrmProspectingCampaignRepository : ISaasRepository<SysCrmProspectingCampaign>
{
    /// <summary>Read the newest campaigns visible to the resolved data scope with the limit applied in SQL.</summary>
    Task<IReadOnlyList<SysCrmProspectingCampaign>> GetLatestAccessibleAsync(bool unrestricted, IReadOnlyCollection<long> accessibleUserIds, int limit, CancellationToken cancellationToken = default);
}
