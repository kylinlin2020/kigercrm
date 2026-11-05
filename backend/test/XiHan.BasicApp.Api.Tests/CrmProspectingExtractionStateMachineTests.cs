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
    [InlineData(CrmProspectingExtractionStatus.FetchingResults, CrmProspectingExtractionStatus.Completed, true)]
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
}
