using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.Framework.Tasks.BackgroundServices;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Periodically dispatches due recurring schedules. The dispatcher reuses the approved StartRun
/// pipeline and advances occurrences with a compare-and-swap, so this host can run in multiple
/// instances without double-dispatching an occurrence.
/// </summary>
public sealed class CrmProspectingScheduleHostedService
    : XiHanBackgroundServiceBase<CrmProspectingScheduleHostedService>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CrmProspectingScheduleHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<XiHanBackgroundServiceOptions> options,
        ILogger<CrmProspectingScheduleHostedService> logger)
        : base(logger, options, BuildConfig(options))
        => _scopeFactory = scopeFactory;

    protected override async Task<IEnumerable<IBackgroundTaskItem>> FetchWorkItemsAsync(
        int maxCount,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<CrmProspectingScheduleDispatcher>();
        await dispatcher.DispatchDueAsync(
            DateTimeOffset.UtcNow,
            Math.Clamp(maxCount, 1, 50),
            cancellationToken);
        // Dispatch is complete inside this hook; there is no per-item work to enqueue.
        return Array.Empty<IBackgroundTaskItem>();
    }

    protected override Task ProcessItemAsync(IBackgroundTaskItem item, CancellationToken cancellationToken)
        => Task.CompletedTask;

    private static IDynamicServiceConfig BuildConfig(IOptions<XiHanBackgroundServiceOptions> options)
    {
        var config = new DynamicServiceConfig(options);
        config.UpdateMaxConcurrentTasks(1);
        config.UpdateIdleDelay(30_000);
        return config;
    }
}
