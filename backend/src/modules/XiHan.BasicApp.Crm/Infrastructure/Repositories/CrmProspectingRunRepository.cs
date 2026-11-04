using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingRunRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingRun>(clientResolver), ICrmProspectingRunRepository
{
    public async Task<SysCrmProspectingRun?> ClaimQueuedAsync(
        long runId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Conditional update is the concurrency boundary: only one worker can move Queued -> Running.
        var claimed = await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = CrmProspectingRunStatus.Running,
                StartedAt = startedAt,
                CompletedAt = null,
                FailureSummary = null
            },
            run => run.BasicId == runId && run.Status == CrmProspectingRunStatus.Queued,
            cancellationToken);

        if (!claimed)
        {
            return null;
        }

        var rows = await CreateQueryable()
            .Where(run => run.BasicId == runId && run.Status == CrmProspectingRunStatus.Running)
            .Take(1)
            .ToListAsync(cancellationToken);

        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<long>> GetQueuedIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await CreateQueryable()
            .Where(run => run.Status == CrmProspectingRunStatus.Queued)
            .OrderBy(run => run.CreatedTime)
            .Select(run => run.BasicId)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> RecoverInterruptedAsync(
        DateTimeOffset recoveredAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A crashed worker can safely be retried from the immutable strategy snapshot.
        var requeued = await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = CrmProspectingRunStatus.Queued,
                StartedAt = null
            },
            run => run.Status == CrmProspectingRunStatus.Running,
            cancellationToken);

        // Cancellation intent survives process death and must not be converted back to executable work.
        var cancelled = await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = CrmProspectingRunStatus.Cancelled,
                CompletedAt = recoveredAt
            },
            run => run.Status == CrmProspectingRunStatus.CancelRequested,
            cancellationToken);

        return (requeued ? 1 : 0) + (cancelled ? 1 : 0);
    }
}
