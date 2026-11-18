using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped durable Super Search mission repository.</summary>
public interface ICrmProspectingSuperSearchMissionRepository : ISaasRepository<SysCrmProspectingSuperSearchMission>
{
    /// <summary>Find one mission by its tenant-scoped idempotency key.</summary>
    Task<SysCrmProspectingSuperSearchMission?> GetByRequestKeyAsync(string requestKey, CancellationToken cancellationToken = default);

    /// <summary>Recent missions of one campaign, newest first.</summary>
    Task<IReadOnlyList<SysCrmProspectingSuperSearchMission>> GetByCampaignAsync(
        long campaignId,
        int take,
        CancellationToken cancellationToken = default);
}
