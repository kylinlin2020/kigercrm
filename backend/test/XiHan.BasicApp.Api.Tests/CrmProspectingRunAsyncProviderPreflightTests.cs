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
