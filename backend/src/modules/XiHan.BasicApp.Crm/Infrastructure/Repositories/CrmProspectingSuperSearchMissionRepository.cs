using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingSuperSearchMissionRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingSuperSearchMission>(clientResolver), ICrmProspectingSuperSearchMissionRepository
{
    public async Task<SysCrmProspectingSuperSearchMission?> GetByRequestKeyAsync(string requestKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await CreateQueryable()
            .Where(mission => mission.RequestKey == requestKey)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SysCrmProspectingSuperSearchMission>> GetByCampaignAsync(
        long campaignId,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (campaignId <= 0) throw new ArgumentOutOfRangeException(nameof(campaignId));
        if (take is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(take));
        cancellationToken.ThrowIfCancellationRequested();
        return await CreateQueryable()
            .Where(mission => mission.CampaignId == campaignId)
            .OrderByDescending(mission => mission.CreatedTime)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
