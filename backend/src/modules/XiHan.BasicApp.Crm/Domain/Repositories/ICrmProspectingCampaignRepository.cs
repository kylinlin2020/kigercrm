using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Strictly tenant-scoped campaign repository.</summary>
public interface ICrmProspectingCampaignRepository : ISaasRepository<SysCrmProspectingCampaign>
{
    /// <summary>Read the newest campaigns visible to the resolved data scope with the limit applied in SQL.</summary>
    /// <summary>Atomically reserve the next strategy version by advancing the campaign sequence.</summary>
    Task<bool> TryAdvanceStrategyVersionAsync(long campaignId, int expectedSequence, int nextSequence, CancellationToken cancellationToken = default);

    /// <summary>Atomically switch the active strategy pointer when its current value still matches the expected version.</summary>
    Task<bool> TrySetActiveStrategyVersionAsync(long campaignId, int? expectedVersion, int nextVersion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysCrmProspectingCampaign>> GetLatestAccessibleAsync(bool unrestricted, IReadOnlyCollection<long> accessibleUserIds, int limit, CancellationToken cancellationToken = default);
}
