using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Domain.DomainServices;
using XiHan.Framework.Tasks.BackgroundServices;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Periodically expires remote extraction jobs whose provider-side validity window has elapsed.
/// This maintenance worker deliberately does not claim jobs or perform provider I/O.
/// </summary>
public sealed class CrmProspectingExtractionExpiryHostedService
    : XiHanBackgroundServiceBase<CrmProspectingExtractionExpiryHostedService>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CrmProspectingExtractionExpiryHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<XiHanBackgroundServiceOptions> options,
        ILogger<CrmProspectingExtractionExpiryHostedService> logger)
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
        var expiredCount = 0;

        // Extraction jobs are strict tenant data. Never run the repository in platform scope.
        // TenantDataScopeRunner handles field-isolated and configured database-isolated tenants.
        await runner.RunAsync(async tenantId =>
        {
            if (tenantId is null) return;

            using var tenantScope = _scopeFactory.CreateScope();
            var repository = tenantScope.ServiceProvider
                .GetRequiredService<ICrmProspectingExtractionJobRepository>();
            expiredCount += await repository.ExpireDueAsync(cancellationToken);
        }, cancellationToken);

        if (expiredCount > 0)
            Logger.LogInformation(
                "Expired {Count} provider extraction jobs across tenant data scopes",
                expiredCount);

        // Returning no work lets the base worker apply the configured 30-second idle delay.
        return [];
    }

    private static IDynamicServiceConfig BuildConfig(IOptions<XiHanBackgroundServiceOptions> options)
    {
        var config = new DynamicServiceConfig(options);
        config.UpdateMaxConcurrentTasks(1);
        config.UpdateIdleDelay(30_000);
        return config;
    }

    private sealed class ExpirySweepTrigger : IBackgroundTaskItem
    {
        [JsonIgnore]
        public string TaskId => "crm-prospecting-extraction-expiry";

        [JsonIgnore]
        public object? Data => null;

        public DateTimeOffset CreatedAt { get; init; }

        public int RetryCount { get; set; }
    }
}
