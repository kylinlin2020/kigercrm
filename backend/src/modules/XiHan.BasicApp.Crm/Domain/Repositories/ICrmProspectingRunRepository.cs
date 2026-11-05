using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Strictly tenant-scoped prospecting run repository.</summary>
public interface ICrmProspectingRunRepository : ISaasRepository<SysCrmProspectingRun>
{
    /// <summary>Find one Run by its tenant-scoped idempotency key. The inherited tenant filter remains active.</summary>
    Task<SysCrmProspectingRun?> GetByRequestKeyAsync(string requestKey, CancellationToken cancellationToken = default);

    /// <summary>Atomically claim a queued or expired-lease Run using PostgreSQL database time; returns the exact token/version granted by UPDATE RETURNING.</summary>
    Task<SysCrmProspectingRun?> TryClaimLeaseAsync(long runId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>Lock and validate a live lease in the caller's active transactional UnitOfWork before any Candidate/Evidence/Score child writes.</summary>
    Task<bool> TryLockLeaseForWriteAsync(long runId, Guid leaseToken, long fencingVersion, CancellationToken cancellationToken = default);

    /// <summary>Atomically claim a queued run. A duplicate delivery or competing worker receives null.</summary>
    Task<SysCrmProspectingRun?> ClaimQueuedAsync(long runId, DateTimeOffset startedAt, CancellationToken cancellationToken = default);

    /// <summary>Extend a live lease using PostgreSQL database time; stale or expired ownership affects no rows.</summary>
    Task<bool> RenewLeaseWithDatabaseClockAsync(long runId, Guid leaseToken, long fencingVersion, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>Extend a live lease only when the exact tenant-scoped token/version still owns an unexpired Running Run.</summary>
    Task<bool> RenewLeaseAsync(long runId, Guid leaseToken, long fencingVersion, DateTimeOffset heartbeatAt, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>Complete using PostgreSQL database time; worker-supplied timestamps cannot extend ownership.</summary>
    Task<bool> CompleteFencedWithDatabaseClockAsync(long runId, Guid leaseToken, long fencingVersion, string rawResultsJson, int rawRecordCount, int failureCount, string? failureSummary, CancellationToken cancellationToken = default, int candidateCount = 0);

    /// <summary>Finalize cancellation using PostgreSQL database time and exact lease identity.</summary>
    Task<bool> CancelFencedWithDatabaseClockAsync(long runId, Guid leaseToken, long fencingVersion, CancellationToken cancellationToken = default);

    /// <summary>Fail using PostgreSQL database time and exact lease identity.</summary>
    Task<bool> FailFencedWithDatabaseClockAsync(long runId, Guid leaseToken, long fencingVersion, string failureSummary, CancellationToken cancellationToken = default);

    /// <summary>Complete only when the exact lease token/version still owns an unexpired Running Run.</summary>
    Task<bool> CompleteFencedAsync(long runId, Guid leaseToken, long fencingVersion, DateTimeOffset now, string rawResultsJson, int rawRecordCount, int failureCount, string? failureSummary, DateTimeOffset completedAt, CancellationToken cancellationToken = default, int candidateCount = 0);

    /// <summary>Finalize cancellation only when the exact lease token/version still owns the CancelRequested Run.</summary>
    Task<bool> CancelFencedAsync(long runId, Guid leaseToken, long fencingVersion, DateTimeOffset now, DateTimeOffset completedAt, CancellationToken cancellationToken = default);

    /// <summary>Fail only when the exact lease token/version still owns an unexpired Running Run.</summary>
    Task<bool> FailFencedAsync(long runId, Guid leaseToken, long fencingVersion, DateTimeOffset now, string failureSummary, DateTimeOffset completedAt, CancellationToken cancellationToken = default);

    /// <summary>Read the newest runs for one campaign with the result limit applied in SQL.</summary>
    Task<IReadOnlyList<SysCrmProspectingRun>> GetLatestByCampaignAsync(long campaignId, int limit, CancellationToken cancellationToken = default);

    /// <summary>List queued run IDs in creation order for startup/queue-loss recovery in the current tenant scope.</summary>
    Task<IReadOnlyList<long>> GetQueuedIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Requeue expired Running leases and finalize expired CancelRequested leases as Cancelled, using PostgreSQL database time. Does not activate legacy lease-less rows.</summary>
    Task<int> RecoverExpiredLeasesAsync(CancellationToken cancellationToken = default);

    /// <summary>Single-active-worker startup recovery only: returns true if any rows transitioned; Running returns to Queued and CancelRequested becomes Cancelled. The result is not a row count. Do not run concurrently with live workers; multi-instance recovery requires leases/fencing.</summary>
    Task<bool> RecoverInterruptedAsync(DateTimeOffset recoveredAt, CancellationToken cancellationToken = default);

    /// <summary>Persist provider output and transition a claimed run to its terminal state.</summary>
    Task<bool> CompleteAsync(long runId, string rawResultsJson, int rawRecordCount, int failureCount, string? failureSummary, DateTimeOffset completedAt, CancellationToken cancellationToken = default, int candidateCount = 0);

    /// <summary>Mark a claimed or cancellation-requested run as failed/cancelled without overwriting another terminal state.</summary>
    Task<bool> FailAsync(long runId, string failureSummary, DateTimeOffset completedAt, CancellationToken cancellationToken = default);
}
