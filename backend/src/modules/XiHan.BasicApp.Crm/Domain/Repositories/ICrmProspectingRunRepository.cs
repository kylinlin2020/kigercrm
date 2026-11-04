using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Strictly tenant-scoped prospecting run repository.</summary>
public interface ICrmProspectingRunRepository : ISaasRepository<SysCrmProspectingRun>
{
    /// <summary>Find one Run by its tenant-scoped idempotency key. The inherited tenant filter remains active.</summary>
    Task<SysCrmProspectingRun?> GetByRequestKeyAsync(string requestKey, CancellationToken cancellationToken = default);

    /// <summary>Atomically claim a queued run. A duplicate delivery or competing worker receives null.</summary>
    Task<SysCrmProspectingRun?> ClaimQueuedAsync(long runId, DateTimeOffset startedAt, CancellationToken cancellationToken = default);

    /// <summary>Extend a live lease only when the exact tenant-scoped token/version still owns an unexpired Running Run.</summary>
    Task<bool> RenewLeaseAsync(long runId, Guid leaseToken, long fencingVersion, DateTimeOffset heartbeatAt, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>Complete only when the exact lease token/version still owns an unexpired Running Run.</summary>
    Task<bool> CompleteFencedAsync(long runId, Guid leaseToken, long fencingVersion, DateTimeOffset now, string rawResultsJson, int rawRecordCount, int failureCount, string? failureSummary, DateTimeOffset completedAt, CancellationToken cancellationToken = default, int candidateCount = 0);

    /// <summary>Fail only when the exact lease token/version still owns an unexpired Running Run.</summary>
    Task<bool> FailFencedAsync(long runId, Guid leaseToken, long fencingVersion, DateTimeOffset now, string failureSummary, DateTimeOffset completedAt, CancellationToken cancellationToken = default);

    /// <summary>Read the newest runs for one campaign with the result limit applied in SQL.</summary>
    Task<IReadOnlyList<SysCrmProspectingRun>> GetLatestByCampaignAsync(long campaignId, int limit, CancellationToken cancellationToken = default);

    /// <summary>List queued run IDs in creation order for startup/queue-loss recovery in the current tenant scope.</summary>
    Task<IReadOnlyList<long>> GetQueuedIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Single-active-worker startup recovery only: returns true if any rows transitioned; Running returns to Queued and CancelRequested becomes Cancelled. The result is not a row count. Do not run concurrently with live workers; multi-instance recovery requires leases/fencing.</summary>
    Task<bool> RecoverInterruptedAsync(DateTimeOffset recoveredAt, CancellationToken cancellationToken = default);

    /// <summary>Persist provider output and transition a claimed run to its terminal state.</summary>
    Task<bool> CompleteAsync(long runId, string rawResultsJson, int rawRecordCount, int failureCount, string? failureSummary, DateTimeOffset completedAt, CancellationToken cancellationToken = default, int candidateCount = 0);

    /// <summary>Mark a claimed or cancellation-requested run as failed/cancelled without overwriting another terminal state.</summary>
    Task<bool> FailAsync(long runId, string failureSummary, DateTimeOffset completedAt, CancellationToken cancellationToken = default);
}
