using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped persistence and lease operations for asynchronous provider extraction jobs.</summary>
public interface ICrmProspectingExtractionJobRepository : ISaasRepository<SysCrmProspectingExtractionJob>
{
    /// <summary>Creates the local Submitting intent before any provider network call; repeated calls return the same tenant-scoped intent.</summary>
    Task<SysCrmProspectingExtractionJob> CreateSubmissionIntentAsync(
        long runId,
        string providerCode,
        string requestFingerprint,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Persists the remote acknowledgement only while the exact live lease owns a Submitting intent.</summary>
    Task<bool> TryAcknowledgeSubmissionAsync(
        long jobId,
        Guid leaseToken,
        long fencingVersion,
        string providerExtractionId,
        DateTimeOffset submittedAtUtc,
        DateTimeOffset nextPollAtUtc,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken = default);

    Task<SysCrmProspectingExtractionJob> CreateAsync(
        long runId,
        string providerCode,
        string requestFingerprint,
        string providerExtractionId,
        DateTimeOffset submittedAtUtc,
        DateTimeOffset nextPollAtUtc,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken = default);

    Task<SysCrmProspectingExtractionJob?> GetByIdAsync(
        long jobId,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically claims due jobs using PostgreSQL SKIP LOCKED and increments their fencing version.</summary>
    Task<IReadOnlyList<SysCrmProspectingExtractionJob>> ClaimDueAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>Marks provider jobs expired by database time and releases any stale lease.</summary>
    Task<int> ExpireDueAsync(CancellationToken cancellationToken = default);

    /// <summary>Conditionally changes lifecycle state under the exact tenant/job/lease identity.</summary>
    Task<bool> TryTransitionAsync(
        long jobId,
        CrmProspectingExtractionStatus expectedStatus,
        CrmProspectingExtractionStatus nextStatus,
        Guid leaseToken,
        long fencingVersion,
        DateTimeOffset? nextPollAtUtc = null,
        DateTimeOffset? lastPolledAtUtc = null,
        string? failureCode = null,
        string? safeFailureSummary = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a result cursor only after the corresponding page records/evidence have been committed
    /// in the same database transaction. The caller must establish that transaction.
    /// </summary>
    Task<bool> TryAdvanceCursorAsync(
        long jobId,
        Guid leaseToken,
        long fencingVersion,
        string? expectedCursor,
        string? nextCursor,
        int persistedRecordCount,
        CancellationToken cancellationToken = default);
}
