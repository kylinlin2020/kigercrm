using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Domain.DomainServices;
using XiHan.Framework.Tasks.BackgroundServices;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Periodically recovers idle customer-pool claims across tenant data scopes. The same
/// reclamation function backs the manual API, so scheduled and manual runs behave identically.
/// No external I/O and no automatic outreach is performed.
/// </summary>
public sealed class CrmCustomerPoolRecoveryHostedService
    : XiHanBackgroundServiceBase<CrmCustomerPoolRecoveryHostedService>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CrmCustomerPoolRecoveryHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<XiHanBackgroundServiceOptions> options,
        ILogger<CrmCustomerPoolRecoveryHostedService> logger)
        : base(logger, options, BuildConfig(options))
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task<IEnumerable<IBackgroundTaskItem>> FetchWorkItemsAsync(
        int maxCount,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITenantDataScopeRunner>();
        var recovered = 0;

        // Pool entries are strict tenant data. Never run the repository in platform scope.
        await runner.RunAsync(async tenantId =>
        {
            if (tenantId is null) return;

            using var tenantScope = _scopeFactory.CreateScope();
            var repository = tenantScope.ServiceProvider.GetRequiredService<ICrmCustomerPoolRepository>();
            recovered += await CrmCustomerPoolReclamation.RecoverIdleAsync(repository, cancellationToken);
        }, cancellationToken);

        if (recovered > 0)
            Logger.LogInformation("Recovered {Count} idle customer-pool claims across tenant data scopes", recovered);

        // No work items: the base worker applies the configured idle delay between sweeps.
        return [];
    }

    protected override Task ProcessItemAsync(
        IBackgroundTaskItem item,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    private static IDynamicServiceConfig BuildConfig(IOptions<XiHanBackgroundServiceOptions> options)
    {
        var config = new DynamicServiceConfig(options);
        config.UpdateMaxConcurrentTasks(1);
        config.UpdateIdleDelay(60_000);
        return config;
    }
}
