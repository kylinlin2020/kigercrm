using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped append-only provider invocation ledger.</summary>
public interface ICrmProspectingProviderRequestRepository : ISaasRepository<SysCrmProspectingProviderRequest>
{
    /// <summary>
    /// Persist Started before the network call. Concurrent allocators may race on the unique attempt key;
    /// callers must retry this operation before making any provider request.
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

    /// <summary>Mark stale Started attempts interrupted only after their owning Run is no longer actively leased.</summary>
    Task<int> RecoverStaleStartedAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default);

    /// <summary>Find one attempt by tenant-scoped Run/provider/attempt identity.</summary>
    Task<SysCrmProspectingProviderRequest?> GetAttemptAsync(
        long runId, string providerCode, int attemptNo,
        CancellationToken cancellationToken = default);
}
