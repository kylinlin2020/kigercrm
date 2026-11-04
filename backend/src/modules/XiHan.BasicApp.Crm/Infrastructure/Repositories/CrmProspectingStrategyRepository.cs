using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingStrategyRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingStrategy>(clientResolver), ICrmProspectingStrategyRepository
{
    public async Task<bool> TryTransitionApprovalAsync(long strategyId, CrmProspectingStrategyApprovalStatus expectedStatus, CrmProspectingStrategyApprovalStatus nextStatus, long? approverId, DateTimeOffset decidedAt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await UpdateAsync(
            strategy => new SysCrmProspectingStrategy
            {
                ApprovalStatus = nextStatus,
                ApprovedBy = approverId,
                ApprovedAt = decidedAt
            },
            strategy => strategy.BasicId == strategyId && strategy.ApprovalStatus == expectedStatus,
            cancellationToken);
    }

    public async Task<IReadOnlyList<SysCrmProspectingStrategy>> GetLatestByCampaignAsync(long campaignId, int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var take = Math.Clamp(limit, 1, 500);
        return await CreateQueryable()
            .Where(item => item.CampaignId == campaignId)
            .OrderByDescending(item => item.Version)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
