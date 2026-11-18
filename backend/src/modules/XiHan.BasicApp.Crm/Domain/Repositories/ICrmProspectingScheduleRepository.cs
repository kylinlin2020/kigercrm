using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped recurring schedule repository with a compare-and-swap dispatch claim.</summary>
public interface ICrmProspectingScheduleRepository : ISaasRepository<SysCrmProspectingSchedule>
{
    /// <summary>Find one schedule by its tenant-scoped idempotency key prefix.</summary>
    Task<SysCrmProspectingSchedule?> GetByRequestKeyAsync(string requestKeyPrefix, CancellationToken cancellationToken = default);

    /// <summary>Approved schedules whose next occurrence is due, oldest due first.</summary>
    Task<IReadOnlyList<SysCrmProspectingSchedule>> GetDueAsync(
        DateTimeOffset nowUtc,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advance one schedule only if its next occurrence is still the value the worker observed;
    /// exactly one of two concurrent workers can win for the same occurrence.
    /// </summary>
    Task<bool> TryClaimAsync(
        long scheduleId,
        DateTimeOffset expectedNextRunAtUtc,
        DateTimeOffset? claimedNextRunAtUtc,
        DateTimeOffset nowUtc,
        long runId,
        CancellationToken cancellationToken = default);
}
