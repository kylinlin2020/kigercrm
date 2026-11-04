using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Strictly tenant-scoped immutable strategy-version repository.</summary>
public interface ICrmProspectingStrategyRepository : ISaasRepository<SysCrmProspectingStrategy>
{
    /// <summary>Atomically transition a strategy only when its persisted approval state matches expectedStatus.</summary>
    Task<bool> TryTransitionApprovalAsync(long strategyId, CrmProspectingStrategyApprovalStatus expectedStatus, CrmProspectingStrategyApprovalStatus nextStatus, long? approverId, DateTimeOffset decidedAt, CancellationToken cancellationToken = default);

    /// <summary>Clear activation timestamps from every other version in a campaign.</summary>
    Task<bool> ClearOtherActiveVersionsAsync(long campaignId, long keepStrategyId, CancellationToken cancellationToken = default);

    /// <summary>Mark an approved strategy version as activated using a conditional update.</summary>
    Task<bool> TryMarkActivatedAsync(long campaignId, long strategyId, DateTimeOffset activatedAt, CancellationToken cancellationToken = default);

    /// <summary>Read the newest strategy versions for one campaign with the result limit applied in SQL.</summary>
    Task<IReadOnlyList<SysCrmProspectingStrategy>> GetLatestByCampaignAsync(long campaignId, int limit, CancellationToken cancellationToken = default);
}
