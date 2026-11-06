using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingExtractionWorkActionPolicyTests
{
    [Fact]
    public void SubmittingWithoutRemoteIdentitySelectsSubmit()
    {
        var decision = CrmProspectingExtractionWorkActionPolicy.Evaluate(
            CrmProspectingExtractionStatus.Submitting, null);

        Assert.Equal(CrmProspectingExtractionWorkAction.Submit, decision.Action);
        Assert.Null(decision.BlockReason);
    }

    [Fact]
    public void SubmittingWithRemoteIdentityIsBlockedFromDuplicateSubmission()
    {
        var decision = CrmProspectingExtractionWorkActionPolicy.Evaluate(
            CrmProspectingExtractionStatus.Submitting, "remote-123");

        Assert.Equal(CrmProspectingExtractionWorkAction.None, decision.Action);
        Assert.Equal("submitting_has_remote_identity", decision.BlockReason);
    }

    [Theory]
    [InlineData(CrmProspectingExtractionStatus.Pending)]
    [InlineData(CrmProspectingExtractionStatus.Running)]
    public void PollStatesRequireRemoteIdentity(CrmProspectingExtractionStatus status)
    {
        var missing = CrmProspectingExtractionWorkActionPolicy.Evaluate(status, null);
        var present = CrmProspectingExtractionWorkActionPolicy.Evaluate(status, "remote-123");

        Assert.Equal(CrmProspectingExtractionWorkAction.None, missing.Action);
        Assert.Equal("poll_requires_remote_identity", missing.BlockReason);
        Assert.Equal(CrmProspectingExtractionWorkAction.Poll, present.Action);
    }

    [Theory]
    [InlineData(CrmProspectingExtractionStatus.Succeeded)]
    [InlineData(CrmProspectingExtractionStatus.FetchingResults)]
    public void ResultFetchStatesRequireRemoteIdentity(CrmProspectingExtractionStatus status)
    {
        var missing = CrmProspectingExtractionWorkActionPolicy.Evaluate(status, null);
        var present = CrmProspectingExtractionWorkActionPolicy.Evaluate(status, "remote-123");

        Assert.Equal(CrmProspectingExtractionWorkAction.None, missing.Action);
        Assert.Equal("fetch_requires_remote_identity", missing.BlockReason);
        Assert.Equal(
            status == CrmProspectingExtractionStatus.Succeeded
                ? CrmProspectingExtractionWorkAction.BeginResultFetch
                : CrmProspectingExtractionWorkAction.FetchResults,
            present.Action);
    }

    [Fact]
    public void SucceededStateMustBePersistedAsFetchingBeforePageFetch()
    {
        var decision = CrmProspectingExtractionWorkActionPolicy.Evaluate(
            CrmProspectingExtractionStatus.Succeeded, "remote-123");

        Assert.Equal(CrmProspectingExtractionWorkAction.BeginResultFetch, decision.Action);
    }


    [Theory]
    [InlineData(CrmProspectingExtractionStatus.NeedsReview)]
    [InlineData(CrmProspectingExtractionStatus.Completed)]
    [InlineData(CrmProspectingExtractionStatus.Failed)]
    [InlineData(CrmProspectingExtractionStatus.Expired)]
    [InlineData(CrmProspectingExtractionStatus.Cancelled)]
    public void ManualReviewAndTerminalStatesNeverScheduleRemoteIo(CrmProspectingExtractionStatus status)
    {
        var decision = CrmProspectingExtractionWorkActionPolicy.Evaluate(status, "remote-123");

        Assert.Equal(CrmProspectingExtractionWorkAction.None, decision.Action);
        Assert.Null(decision.BlockReason);
    }

    [Fact]
    public void UnknownLocalNumericStateFailsClosed()
    {
        var decision = CrmProspectingExtractionWorkActionPolicy.Evaluate(
            (CrmProspectingExtractionStatus)999, "remote-123");

        Assert.Equal(CrmProspectingExtractionWorkAction.None, decision.Action);
        Assert.Equal("unknown_local_status", decision.BlockReason);
    }
}
