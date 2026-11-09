using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingExtractionStateMachineTests
{
    [Theory]
    [InlineData(CrmProspectingExtractionStatus.Submitting, CrmProspectingExtractionStatus.Pending, false)]
    [InlineData(CrmProspectingExtractionStatus.Submitting, CrmProspectingExtractionStatus.NeedsReview, true)]
    [InlineData(CrmProspectingExtractionStatus.Submitting, CrmProspectingExtractionStatus.Failed, true)]
    [InlineData(CrmProspectingExtractionStatus.Submitting, CrmProspectingExtractionStatus.Succeeded, false)]
    [InlineData(CrmProspectingExtractionStatus.Pending, CrmProspectingExtractionStatus.Running, true)]
    [InlineData(CrmProspectingExtractionStatus.Pending, CrmProspectingExtractionStatus.Succeeded, true)]
    [InlineData(CrmProspectingExtractionStatus.Running, CrmProspectingExtractionStatus.Succeeded, true)]
    [InlineData(CrmProspectingExtractionStatus.Running, CrmProspectingExtractionStatus.Failed, true)]
    [InlineData(CrmProspectingExtractionStatus.Succeeded, CrmProspectingExtractionStatus.FetchingResults, true)]
    [InlineData(CrmProspectingExtractionStatus.Succeeded, CrmProspectingExtractionStatus.Cancelled, true)]
    [InlineData(CrmProspectingExtractionStatus.FetchingResults, CrmProspectingExtractionStatus.Completed, true)]
    [InlineData(CrmProspectingExtractionStatus.FetchingResults, CrmProspectingExtractionStatus.Cancelled, true)]
    [InlineData(CrmProspectingExtractionStatus.Completed, CrmProspectingExtractionStatus.Running, false)]
    [InlineData(CrmProspectingExtractionStatus.Pending, CrmProspectingExtractionStatus.NeedsReview, true)]
    [InlineData(CrmProspectingExtractionStatus.NeedsReview, CrmProspectingExtractionStatus.Running, true)]
    [InlineData(CrmProspectingExtractionStatus.Failed, CrmProspectingExtractionStatus.Running, false)]
    [InlineData(CrmProspectingExtractionStatus.Expired, CrmProspectingExtractionStatus.Succeeded, false)]
    [InlineData(CrmProspectingExtractionStatus.Cancelled, CrmProspectingExtractionStatus.Pending, false)]
    [InlineData(CrmProspectingExtractionStatus.Succeeded, CrmProspectingExtractionStatus.Running, false)]
    public void CanTransition_ShouldEnforceLifecycle(
        CrmProspectingExtractionStatus current,
        CrmProspectingExtractionStatus next,
        bool expected)
        => Assert.Equal(expected, CrmProspectingExtractionStateMachine.CanTransition(current, next));

    [Fact]
    public void SubmittingToPending_ShouldRequireAcknowledgementPersistenceBoundary()
        => Assert.False(CrmProspectingExtractionStateMachine.CanTransition(
            CrmProspectingExtractionStatus.Submitting,
            CrmProspectingExtractionStatus.Pending));

    [Theory]
    [InlineData(false, true, 0, false)]
    [InlineData(true, false, 0, false)]
    [InlineData(true, true, 1, false)]
    [InlineData(true, true, 0, true)]
    public void CanCompleteResults_ShouldRequireFullyPersistableFinalPage(
        bool isLastPage, bool isComplete, int failureCount, bool expected)
        => Assert.Equal(expected, CrmProspectingExtractionStateMachine.CanCompleteResults(
            CrmProspectingExtractionStatus.FetchingResults, isLastPage, isComplete, failureCount));

    [Theory]
    [InlineData(CrmProspectingExtractionStatus.Submitting, false)]
    [InlineData(CrmProspectingExtractionStatus.NeedsReview, false)]
    [InlineData(CrmProspectingExtractionStatus.Pending, true)]
    [InlineData(CrmProspectingExtractionStatus.Running, true)]
    [InlineData(CrmProspectingExtractionStatus.Succeeded, true)]
    [InlineData(CrmProspectingExtractionStatus.FetchingResults, true)]
    [InlineData(CrmProspectingExtractionStatus.Completed, true)]
    [InlineData(CrmProspectingExtractionStatus.Failed, false)]
    [InlineData(CrmProspectingExtractionStatus.Expired, false)]
    [InlineData(CrmProspectingExtractionStatus.Cancelled, false)]
    public void RequiresProviderExtractionId_ShouldMatchRemoteLifecycleStates(
        CrmProspectingExtractionStatus status, bool expected)
        => Assert.Equal(expected, CrmProspectingExtractionStateMachine.RequiresProviderExtractionId(status));

    [Fact]
    public void CanCompleteResults_ShouldRejectCompletionBeforeResultFetching()
        => Assert.False(CrmProspectingExtractionStateMachine.CanCompleteResults(
            CrmProspectingExtractionStatus.Running, true, true, 0));


    [Fact]
    public void AsyncSubmitContract_ShouldRequireStableIdempotencyKey()
    {
        var submit = typeof(ICrmProspectingAsyncExtractionProvider).GetMethod(
            nameof(ICrmProspectingAsyncExtractionProvider.SubmitExtractionAsync));
        Assert.NotNull(submit);
        Assert.Contains(submit!.GetParameters(), parameter => parameter.ParameterType == typeof(Guid));
    }

    [Fact]
    public void SynchronousProviderContract_ShouldRemainIndependent()
    {
        Assert.Contains(typeof(ICrmProspectingProvider).GetMethods(),
            method => method.Name == nameof(ICrmProspectingProvider.SearchAsync));
        Assert.DoesNotContain(typeof(ICrmProspectingProvider).GetMethods(),
            method => method.Name == nameof(ICrmProspectingAsyncExtractionProvider.SubmitExtractionAsync));
    }

    [Fact]
    public void ExtractionQueueMessage_ShouldBeBoundToClaimedLeaseGeneration()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Prospecting", "CrmProspectingExtractionHostedService.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("public Guid LeaseToken { get; init; }", source, StringComparison.Ordinal);
        Assert.Contains("public long FencingVersion { get; init; }", source, StringComparison.Ordinal);
        Assert.Contains("FencingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)", source, StringComparison.Ordinal);
        Assert.Contains("LeaseToken = job.LeaseToken", source, StringComparison.Ordinal);
        Assert.Contains("FencingVersion = job.FencingVersion", source, StringComparison.Ordinal);

        var leaseCheck = source.IndexOf("job.LeaseToken != message.LeaseToken", StringComparison.Ordinal);
        var fenceCheck = source.IndexOf("job.FencingVersion != message.FencingVersion", StringComparison.Ordinal);
        var renewBeforeWork = source.IndexOf(
            "if (!await jobs.TryRenewLeaseAsync(", fenceCheck, StringComparison.Ordinal);
        var heartbeatStart = source.IndexOf("var heartbeat = MaintainLeaseAsync", StringComparison.Ordinal);
        Assert.True(leaseCheck >= 0);
        Assert.True(fenceCheck > leaseCheck);
        Assert.True(renewBeforeWork > fenceCheck);
        Assert.True(heartbeatStart > renewBeforeWork);
    }


    [Fact]
    public void CancelledParentRun_ShouldStopRemoteExtractionWork()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Prospecting", "CrmProspectingExtractionHostedService.cs");
        var source = File.ReadAllText(path);

        var parentCancellation = source.IndexOf(
            "run.Status is CrmProspectingRunStatus.CancelRequested or CrmProspectingRunStatus.Cancelled",
            StringComparison.Ordinal);
        Assert.True(parentCancellation >= 0);
        var ambiguousSubmitGuard = source.IndexOf(
            "status == CrmProspectingExtractionStatus.Submitting",
            parentCancellation,
            StringComparison.Ordinal);
        var providerCancellation = source.IndexOf(
            "provider.CancelExtractionAsync",
            parentCancellation,
            StringComparison.Ordinal);
        var requestReconstruction = source.IndexOf(
            "CrmProspectingProviderRequestFactory.Create",
            parentCancellation,
            StringComparison.Ordinal);

        Assert.True(ambiguousSubmitGuard > parentCancellation);
        Assert.True(providerCancellation > ambiguousSubmitGuard);
        Assert.True(requestReconstruction > providerCancellation);
    }

    [Fact]
    public void TerminalParentRun_ShouldStopRemoteExtractionWork()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Prospecting", "CrmProspectingExtractionHostedService.cs");
        var source = File.ReadAllText(path);

        var parentGuard = source.IndexOf(
            "run.Status is CrmProspectingRunStatus.CancelRequested",
            StringComparison.Ordinal);
        Assert.True(parentGuard >= 0);
        var requestReconstruction = source.IndexOf(
            "CrmProspectingProviderRequestFactory.Create",
            parentGuard,
            StringComparison.Ordinal);
        Assert.True(requestReconstruction > parentGuard);

        var guard = source[parentGuard..requestReconstruction];
        Assert.Contains("CrmProspectingRunStatus.Cancelled", guard, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunStatus.Succeeded", guard, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunStatus.PartiallySucceeded", guard, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunStatus.Failed", guard, StringComparison.Ordinal);
        Assert.Contains("provider.CancelExtractionAsync", guard, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingExtractionStatus.Cancelled", guard, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
