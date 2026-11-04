using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped append-only provider invocation ledger.</summary>
public interface ICrmProspectingProviderRequestRepository : ISaasRepository<SysCrmProspectingProviderRequest>
{
    /// <summary>
    /// Persist Started before network I/O. The repository retries bounded unique-key collisions;
    /// if allocation still fails, no provider request may be issued.
    /// </summary>
    Task<SysCrmProspectingProviderRequest> CreateStartedAsync(
        long runId, string providerCode, string requestKey, DateTimeOffset startedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Finalize a Started attempt exactly once. A false result means it was already finalized or is not tenant-visible.</summary>
    Task<bool> TryFinalizeAsync(
        long requestId, string terminalState, DateTimeOffset completedAt, long durationMs,
        int resultCount = 0, int? httpStatus = null, string? failureCode = null,
        string? failureSummary = null, DateTimeOffset? retryAfterAt = null,
        string? responseHash = null, bool cacheHit = false,
        CancellationToken cancellationToken = default);

    /// <summary>Return the latest active tenant/provider cooldown persisted by a RateLimited attempt, if any.</summary>
    Task<DateTimeOffset?> GetActiveCooldownUntilAsync(string providerCode, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Read the tenant/campaign/provider/scope checkpoint used to seed an incremental provider window.</summary>
    Task<SysCrmProspectingProviderCoverage?> GetCoverageCheckpointAsync(
        long campaignId, string providerCode, string coverageScopeHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically finalize a complete successful request and advance its campaign/provider coverage checkpoint.
    /// Partial, truncated or failed results must use TryFinalizeAsync instead.
    /// </summary>
    Task<bool> TryFinalizeSuccessWithCoverageAsync(
        long requestId, long campaignId, string coverageScopeHash,
        DateTimeOffset completedAt, long durationMs, int resultCount, string responseHash,
        CancellationToken cancellationToken = default);

    /// <summary>Apply PostgreSQL CHECK constraints after entity CodeFirst initialization, idempotently.</summary>
    Task EnsureSchemaConstraintsAsync(CancellationToken cancellationToken = default);

    /// <summary>Mark stale Started attempts interrupted only after their owning Run is no longer actively leased.</summary>
    Task<int> RecoverStaleStartedAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default);

    /// <summary>Find one attempt by tenant-scoped Run/provider/attempt identity.</summary>
    Task<SysCrmProspectingProviderRequest?> GetAttemptAsync(
        long runId, string providerCode, int attemptNo,
        CancellationToken cancellationToken = default);
}
