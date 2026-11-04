using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Strictly tenant-scoped prospecting run repository.</summary>
public interface ICrmProspectingRunRepository : ISaasRepository<SysCrmProspectingRun>
{
    /// <summary>Atomically claim a queued run. A duplicate delivery or competing worker receives null.</summary>
    Task<SysCrmProspectingRun?> ClaimQueuedAsync(long runId, DateTimeOffset startedAt, CancellationToken cancellationToken = default);

    /// <summary>List queued run IDs in creation order for startup/queue-loss recovery in the current tenant scope.</summary>
    Task<IReadOnlyList<long>> GetQueuedIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Recover interrupted work in the current tenant scope: Running returns to Queued; CancelRequested becomes Cancelled.</summary>
    Task<int> RecoverInterruptedAsync(DateTimeOffset recoveredAt, CancellationToken cancellationToken = default);
}
