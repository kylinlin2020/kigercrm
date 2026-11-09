using System.Runtime.CompilerServices;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Protects the synchronous Run worker from partially executing mixed sync/async plans.</summary>
public sealed class CrmProspectingRunAsyncProviderPreflightTests
{
    [Fact]
    public void RunStatusValues_ShouldRemainStableBecauseTheyArePersistedAsIntegers()
    {
        Assert.Equal(0, (int)CrmProspectingRunStatus.Queued);
        Assert.Equal(1, (int)CrmProspectingRunStatus.Running);
        Assert.Equal(2, (int)CrmProspectingRunStatus.Succeeded);
        Assert.Equal(3, (int)CrmProspectingRunStatus.PartiallySucceeded);
        Assert.Equal(4, (int)CrmProspectingRunStatus.Failed);
        Assert.Equal(5, (int)CrmProspectingRunStatus.CancelRequested);
        Assert.Equal(6, (int)CrmProspectingRunStatus.Cancelled);
        Assert.Equal(7, (int)CrmProspectingRunStatus.WaitingForExtractions);
    }

    [Fact]
    public void WaitingForExtractions_ShouldBeAnExplicitNonQueuedLifecycleState()
    {
        var enumSource = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Domain", "Entities", "Enums", "CrmProspectingRunStatus.cs");
        var repositorySource = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");

        Assert.Contains("WaitingForExtractions = 7", enumSource, StringComparison.Ordinal);
        var queuedRecoveryStart = repositorySource.IndexOf("GetQueuedIdsAsync", StringComparison.Ordinal);
        var queuedRecoveryEnd = repositorySource.IndexOf("RecoverExpiredLeasesAsync", queuedRecoveryStart, StringComparison.Ordinal);
        Assert.True(queuedRecoveryStart >= 0);
        Assert.True(queuedRecoveryEnd > queuedRecoveryStart);
        var queuedRecovery = repositorySource[queuedRecoveryStart..queuedRecoveryEnd];
        Assert.Contains(".Where(run => run.Status == CrmProspectingRunStatus.Queued)", queuedRecovery, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitingForExtractions", queuedRecovery, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitingRunCancellation_ShouldTransitionDirectlyToCancelled()
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Domain", "DomainServices", "Implementations", "CrmProspectingDomainService.cs");

        Assert.Contains(
            "run.Status is CrmProspectingRunStatus.Queued or CrmProspectingRunStatus.WaitingForExtractions",
            source,
            StringComparison.Ordinal);
        Assert.Contains("run.Status = CrmProspectingRunStatus.Cancelled", source, StringComparison.Ordinal);
    }


    [Fact]
    public void ParentRunCompletion_ShouldWaitForEveryExtractionIncludingNeedsReview()
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");

        var completionStart = source.IndexOf("CompleteFencedWithDatabaseClockAsync", StringComparison.Ordinal);
        var completionEnd = source.IndexOf("CancelFencedWithDatabaseClockAsync", completionStart, StringComparison.Ordinal);
        Assert.True(completionStart >= 0);
        Assert.True(completionEnd > completionStart);
        var completion = source[completionStart..completionEnd];

        Assert.Contains("NOT EXISTS (", completion, StringComparison.Ordinal);
        Assert.Contains("pending_job.status NOT IN (", completion, StringComparison.Ordinal);
        Assert.Contains("@completedExtractionStatus", completion, StringComparison.Ordinal);
        Assert.Contains("@failedExtractionStatus", completion, StringComparison.Ordinal);
        Assert.Contains("@expiredExtractionStatus", completion, StringComparison.Ordinal);
        Assert.Contains("@cancelledExtractionStatus", completion, StringComparison.Ordinal);
        // NeedsReview is intentionally absent from the terminal allow-list, so it blocks parent completion.
        Assert.DoesNotContain("@needsReviewStatus", completion, StringComparison.Ordinal);
    }

    [Fact]
    public void RunWorker_ShouldRejectAsyncPlanBeforeStartingAnySynchronousProvider()
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");

        var preflight = source.IndexOf("unsupportedAsyncProviders", StringComparison.Ordinal);
        var asyncRegistryCheck = source.IndexOf("providerRegistry.Asynchronous.ContainsKey", StringComparison.Ordinal);
        var preflightFailure = source.IndexOf(
            "not yet supported by durable Run orchestration", StringComparison.Ordinal);
        var providerExecutionLoop = source.IndexOf(
            "foreach (var providerCode in plan.Distinct", StringComparison.Ordinal);
        var externalProviderCall = source.IndexOf("provider.SearchAsync(request", StringComparison.Ordinal);

        Assert.True(preflight >= 0);
        Assert.True(asyncRegistryCheck > preflight);
        Assert.True(preflightFailure > asyncRegistryCheck);
        Assert.True(providerExecutionLoop > preflightFailure);
        Assert.True(externalProviderCall > providerExecutionLoop);
    }

    [Fact]
    public void RunWorker_ShouldResolveValidatedRegistryBeforePreflight()
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");

        Assert.Contains(
            "GetRequiredService<CrmProspectingProviderRegistry>()",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "!providers.ContainsKey(providerCode) &&",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "providerRegistry.Asynchronous.ContainsKey(providerCode)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncPreflightFailure_ShouldRemainFencedAndMustNotBeSilentlyConvertedToWaiting()
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");

        var cancellationCatch = source.IndexOf(
            "catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)",
            StringComparison.Ordinal);
        var genericCatch = cancellationCatch < 0
            ? -1
            : source.IndexOf(
                "catch (Exception ex)", cancellationCatch,
                StringComparison.Ordinal);
        var fencedFailure = genericCatch < 0
            ? -1
            : source.IndexOf(
                "FailFencedWithDatabaseClockAsync", genericCatch,
                StringComparison.Ordinal);

        Assert.True(cancellationCatch >= 0);
        Assert.True(genericCatch > cancellationCatch);
        Assert.True(fencedFailure > genericCatch);
        Assert.Contains(
            "message.RunId, failureLeaseToken, activeFencingVersion, BuildFailureSummary(ex), cancellationToken",
            source,
            StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
