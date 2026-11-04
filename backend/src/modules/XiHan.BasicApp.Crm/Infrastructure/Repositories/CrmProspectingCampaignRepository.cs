using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingCampaignRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingCampaign>(clientResolver), ICrmProspectingCampaignRepository
{
    public async Task<bool> TryAdvanceStrategyVersionAsync(long campaignId, int expectedSequence, int nextSequence, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await UpdateAsync(
            campaign => new SysCrmProspectingCampaign { StrategyVersionSequence = nextSequence },
            campaign => campaign.BasicId == campaignId &&
                campaign.StrategyVersionSequence == expectedSequence &&
                campaign.Status != CrmProspectingCampaignStatus.Completed &&
                campaign.Status != CrmProspectingCampaignStatus.Archived,
            cancellationToken);
    }

    public async Task<bool> TrySetActiveStrategyVersionAsync(long campaignId, int? expectedVersion, int nextVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await UpdateAsync(
            campaign => new SysCrmProspectingCampaign
            {
                ActiveStrategyVersion = nextVersion,
                Status = CrmProspectingCampaignStatus.Active
            },
            campaign => campaign.BasicId == campaignId &&
                campaign.ActiveStrategyVersion == expectedVersion &&
                campaign.Status != CrmProspectingCampaignStatus.Completed &&
                campaign.Status != CrmProspectingCampaignStatus.Archived,
            cancellationToken);
    }

    public async Task<IReadOnlyList<SysCrmProspectingCampaign>> GetLatestAccessibleAsync(bool unrestricted, IReadOnlyCollection<long> accessibleUserIds, int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var take = Math.Clamp(limit, 1, 500);
        var query = CreateQueryable();
        if (!unrestricted)
        {
            var userIds = accessibleUserIds.Distinct().ToArray();
            if (userIds.Length == 0) return [];
            query = query.Where(item => item.OwnerUserId.HasValue && userIds.Contains(item.OwnerUserId.Value));
        }
        return await query
            .OrderByDescending(item => item.CreatedTime)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
