using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingScheduleRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingSchedule>(clientResolver), ICrmProspectingScheduleRepository
{
    public async Task<SysCrmProspectingSchedule?> GetByRequestKeyAsync(string requestKeyPrefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKeyPrefix);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await CreateQueryable()
            .Where(schedule => schedule.RequestKeyPrefix == requestKeyPrefix)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SysCrmProspectingSchedule>> GetDueAsync(
        DateTimeOffset nowUtc,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(take));
        cancellationToken.ThrowIfCancellationRequested();
        return await CreateQueryable()
            .Where(schedule => schedule.Status == CrmProspectingScheduleStatus.Approved
                && schedule.NextRunAtUtc != null
                && schedule.NextRunAtUtc <= nowUtc)
            .OrderBy(schedule => schedule.NextRunAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryClaimAsync(
        long scheduleId,
        DateTimeOffset expectedNextRunAtUtc,
        DateTimeOffset? claimedNextRunAtUtc,
        DateTimeOffset nowUtc,
        long runId,
        CancellationToken cancellationToken = default)
    {
        if (scheduleId <= 0) throw new ArgumentOutOfRangeException(nameof(scheduleId));
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        cancellationToken.ThrowIfCancellationRequested();
        return await UpdateAsync(
            schedule => new SysCrmProspectingSchedule
            {
                NextRunAtUtc = claimedNextRunAtUtc,
                LastRunAtUtc = nowUtc,
                LastRunId = runId
            },
            schedule => schedule.BasicId == scheduleId
                && schedule.Status == CrmProspectingScheduleStatus.Approved
                && schedule.NextRunAtUtc == expectedNextRunAtUtc,
            cancellationToken);
    }
}
