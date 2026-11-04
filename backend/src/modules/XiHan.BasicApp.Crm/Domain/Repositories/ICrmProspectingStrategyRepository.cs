using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Strictly tenant-scoped immutable strategy-version repository.</summary>
public interface ICrmProspectingStrategyRepository : ISaasRepository<SysCrmProspectingStrategy>
{
    /// <summary>Atomically transition a strategy only when its persisted approval state matches expectedStatus.</summary>
    Task<bool> TryTransitionApprovalAsync(long strategyId, CrmProspectingStrategyApprovalStatus expectedStatus, CrmProspectingStrategyApprovalStatus nextStatus, long? approverId, DateTimeOffset decidedAt, CancellationToken cancellationToken = default);

    /// <summary>Read the newest strategy versions for one campaign with the result limit applied in SQL.</summary>
    Task<IReadOnlyList<SysCrmProspectingStrategy>> GetLatestByCampaignAsync(long campaignId, int limit, CancellationToken cancellationToken = default);
}
