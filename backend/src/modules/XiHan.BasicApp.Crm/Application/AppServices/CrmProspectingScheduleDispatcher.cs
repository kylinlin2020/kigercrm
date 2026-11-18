using Microsoft.Extensions.Logging;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>
/// Worker-only dispatcher: it never becomes a Dynamic API endpoint. A Run is created through the
/// existing approved StartRun pipeline with a deterministic occurrence request key, then the
/// schedule's next occurrence is advanced with a compare-and-swap so concurrent workers cannot
/// dispatch the same occurrence twice. A failed StartRun leaves NextRunAtUtc untouched for retry.
/// </summary>
public sealed class CrmProspectingScheduleDispatcher(
    ICrmProspectingScheduleRepository schedules,
    ICrmProspectingAppService prospecting,
    ILogger<CrmProspectingScheduleDispatcher> logger)
{
    public async Task<int> DispatchDueAsync(DateTimeOffset nowUtc, int maxCount, CancellationToken cancellationToken = default)
    {
        if (maxCount is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(maxCount));
        var due = await schedules.GetDueAsync(nowUtc, maxCount, cancellationToken);
        var dispatched = 0;
        foreach (var schedule in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (schedule.NextRunAtUtc is not { } occurrence)
                continue;

            try
            {
                var next = CrmProspectingScheduleAppService.ComputeNext(schedule, occurrence);
                var run = await prospecting.StartRunAsync(new CrmProspectingRunStartDto
                {
                    CampaignId = schedule.CampaignId,
                    RequestKey = BuildRequestKey(schedule.RequestKeyPrefix, occurrence)
                }, cancellationToken);

                if (await schedules.TryClaimAsync(schedule.BasicId, occurrence, next, nowUtc, run.BasicId, cancellationToken))
                {
                    dispatched++;
                }
                else
                {
                    logger.LogDebug("Schedule {ScheduleId} occurrence was claimed by another worker", schedule.BasicId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Honest failure: the occurrence remains due and will be retried on the next pass.
                logger.LogWarning(ex, "Schedule {ScheduleId} dispatch failed; occurrence remains due", schedule.BasicId);
            }
        }
        return dispatched;
    }

    internal static string BuildRequestKey(string requestKeyPrefix, DateTimeOffset occurrence)
        => $"{requestKeyPrefix}:{occurrence.UtcDateTime:yyyyMMddHHmmss}";
}
