using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Domain.DomainServices;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Tasks.BackgroundServices;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

public sealed class CrmProspectingExtractionCoordinatorMessage : IBackgroundTaskItem
{
    public long RunId { get; init; }
    public long TenantId { get; init; }
    public Guid LeaseToken { get; init; }
    public long FencingVersion { get; init; }
    public string TaskId => $"{RunId.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{FencingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    public object? Data => null;
}

/// <summary>
/// Recovers waiting parent Runs and finalizes them only after every child extraction is terminal.
/// NeedsReview deliberately blocks automatic parent completion until an explicit human decision.
/// </summary>
public sealed class CrmProspectingExtractionCoordinatorHostedService
    : XiHanBackgroundServiceBase<CrmProspectingExtractionCoordinatorHostedService>
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly string LeaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly IServiceScopeFactory _scopeFactory;

    public CrmProspectingExtractionCoordinatorHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<XiHanBackgroundServiceOptions> options,
        ILogger<CrmProspectingExtractionCoordinatorHostedService> logger)
        : base(logger, options, BuildConfig(options))
        => _scopeFactory = scopeFactory;

    protected override async Task<IEnumerable<IBackgroundTaskItem>> FetchWorkItemsAsync(
        int maxCount,
        CancellationToken cancellationToken)
    {
        if (maxCount <= 0) return Array.Empty<IBackgroundTaskItem>();
        using var scope = _scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITenantDataScopeRunner>();
        var messages = new List<IBackgroundTaskItem>();

        await runner.RunAsync(async tenantId =>
        {
            if (tenantId is null || messages.Count >= maxCount) return;
            using var tenantScope = _scopeFactory.CreateScope();
            using var currentTenant = tenantScope.ServiceProvider
                .GetRequiredService<ICurrentTenant>().Change(tenantId.Value);
            var services = tenantScope.ServiceProvider;
            var runs = services.GetRequiredService<ICrmProspectingRunRepository>();
            var jobs = services.GetRequiredService<ICrmProspectingExtractionJobRepository>();
            var runIds = await runs.GetWaitingForExtractionsIdsAsync(
                Math.Min(100, maxCount - messages.Count), cancellationToken);

            foreach (var runId in runIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (messages.Count >= maxCount) break;

                var children = await jobs.GetByRunAsync(runId, cancellationToken);
                if (children.Count == 0 || children.Any(job => !IsTerminal(job.Status)))
                    continue;

                var claimed = await runs.TryClaimWaitingForExtractionsLeaseAsync(
                    runId, LeaseOwner, LeaseDuration, cancellationToken);
                if (claimed?.LeaseToken is not Guid token || token == Guid.Empty || claimed.FencingVersion <= 0)
                    continue;

                messages.Add(new CrmProspectingExtractionCoordinatorMessage
                {
                    RunId = runId,
                    TenantId = tenantId.Value,
                    LeaseToken = token,
                    FencingVersion = claimed.FencingVersion
                });
            }
        }, cancellationToken);

        return messages;
    }

    protected override async Task ProcessItemAsync(IBackgroundTaskItem item, CancellationToken cancellationToken)
    {
        if (item is not CrmProspectingExtractionCoordinatorMessage message ||
            message.RunId <= 0 || message.TenantId <= 0 ||
            message.LeaseToken == Guid.Empty || message.FencingVersion <= 0)
        {
            Logger.LogWarning("Discarding malformed extraction coordinator item");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(message.TenantId);
        var services = scope.ServiceProvider;
        var runs = services.GetRequiredService<ICrmProspectingRunRepository>();
        var run = await runs.GetByIdAsync(message.RunId, cancellationToken);
        if (run is null ||
            run.Status != CrmProspectingRunStatus.WaitingForExtractions ||
            run.LeaseToken != message.LeaseToken ||
            run.FencingVersion != message.FencingVersion)
            return;

        var jobs = services.GetRequiredService<ICrmProspectingExtractionJobRepository>();
        var children = await jobs.GetByRunAsync(message.RunId, cancellationToken);
        if (children.Count == 0 || children.Any(job => !IsTerminal(job.Status)))
            return;

        if (children.Any(job => job.ResultRecordCount < 0))
        {
            await runs.FailFencedWithDatabaseClockAsync(
                message.RunId, message.LeaseToken, message.FencingVersion,
                "An asynchronous extraction reported an invalid negative result count.", cancellationToken);
            return;
        }

        var rawRecordCountTotal = children.Sum(job => (long)job.ResultRecordCount);
        if (rawRecordCountTotal > int.MaxValue)
        {
            await runs.FailFencedWithDatabaseClockAsync(
                message.RunId, message.LeaseToken, message.FencingVersion,
                "The aggregated asynchronous result count exceeds the Run storage limit.", cancellationToken);
            return;
        }
        var rawRecordCount = (int)rawRecordCountTotal;
        var candidates = await services.GetRequiredService<ICrmProspectingCandidateRepository>()
            .GetListAsync(candidate => candidate.RunId == message.RunId, cancellationToken);
        var failedChildren = children.Count(job =>
            job.Status is nameof(CrmProspectingExtractionStatus.Failed)
                or nameof(CrmProspectingExtractionStatus.Expired)
                or nameof(CrmProspectingExtractionStatus.Cancelled));
        var summary = failedChildren == 0
            ? null
            : $"{failedChildren} asynchronous provider extraction(s) ended unsuccessfully.";

        var completed = await runs.CompleteFencedWithDatabaseClockAsync(
            message.RunId, message.LeaseToken, message.FencingVersion,
            "[]", rawRecordCount, 0, summary, cancellationToken, candidates.Count);

        if (!completed)
            Logger.LogDebug(
                "Parent Run finalization was deferred because its lease or child-terminal precondition changed: {RunId}",
                message.RunId);
    }

    private static bool IsTerminal(string status)
        => status is nameof(CrmProspectingExtractionStatus.Completed)
            or nameof(CrmProspectingExtractionStatus.Failed)
            or nameof(CrmProspectingExtractionStatus.Expired)
            or nameof(CrmProspectingExtractionStatus.Cancelled);

    private static IDynamicServiceConfig BuildConfig(IOptions<XiHanBackgroundServiceOptions> options)
    {
        var config = new DynamicServiceConfig(options);
        config.UpdateMaxConcurrentTasks(1);
        config.UpdateIdleDelay(5_000);
        return config;
    }
}
