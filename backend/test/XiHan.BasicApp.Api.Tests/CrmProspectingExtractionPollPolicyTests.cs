using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingExtractionPollPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CrmProspectingExtractionProviderStatus.Pending, CrmProspectingExtractionStatus.Pending)]
    [InlineData(CrmProspectingExtractionProviderStatus.Running, CrmProspectingExtractionStatus.Running)]
    [InlineData(CrmProspectingExtractionProviderStatus.Succeeded, CrmProspectingExtractionStatus.Succeeded)]
    [InlineData(CrmProspectingExtractionProviderStatus.Failed, CrmProspectingExtractionStatus.Failed)]
    [InlineData(CrmProspectingExtractionProviderStatus.Expired, CrmProspectingExtractionStatus.Expired)]
    [InlineData(CrmProspectingExtractionProviderStatus.Cancelled, CrmProspectingExtractionStatus.Cancelled)]
    [InlineData(CrmProspectingExtractionProviderStatus.NeedsReview, CrmProspectingExtractionStatus.NeedsReview)]
    public void Evaluate_MapsKnownProviderStates(
        CrmProspectingExtractionProviderStatus providerStatus,
        CrmProspectingExtractionStatus expectedStatus)
    {
        var decision = CrmProspectingExtractionPollPolicy.Evaluate(
            new(providerStatus, null, null, null), Now, 0);

        Assert.Equal(expectedStatus, decision.NextStatus);
    }

    [Fact]
    public void Evaluate_UnknownNumericStatusFailsClosedToNeedsReview()
    {
        var decision = CrmProspectingExtractionPollPolicy.Evaluate(
            new((CrmProspectingExtractionProviderStatus)999, null, null, null), Now, 0);

        Assert.Equal(CrmProspectingExtractionStatus.NeedsReview, decision.NextStatus);
        Assert.Null(decision.NextPollAtUtc);
        Assert.Equal("unknown_provider_status", decision.FailureCode);
    }

    [Fact]
    public void Evaluate_PendingAndRunningAlwaysReceiveBoundedNextPoll()
    {
        var pending = CrmProspectingExtractionPollPolicy.Evaluate(
            new(CrmProspectingExtractionProviderStatus.Pending, Now.AddDays(10), null, null), Now, 0);
        var running = CrmProspectingExtractionPollPolicy.Evaluate(
            new(CrmProspectingExtractionProviderStatus.Running, Now.AddSeconds(-1), null, null), Now, 0);

        Assert.Equal(Now + CrmProspectingExtractionPollSchedule.MaximumDelay, pending.NextPollAtUtc);
        Assert.Equal(Now + CrmProspectingExtractionPollSchedule.MinimumDelay, running.NextPollAtUtc);
    }

    [Fact]
    public void Evaluate_TerminalSuccessDoesNotScheduleAnotherPoll()
    {
        var decision = CrmProspectingExtractionPollPolicy.Evaluate(
            new(CrmProspectingExtractionProviderStatus.Succeeded, Now, null, null), Now, 2);

        Assert.Null(decision.NextPollAtUtc);
        Assert.Null(decision.FailureCode);
    }

    [Fact]
    public void Evaluate_FailureCodeIsNormalizedAndSummaryDoesNotEchoProviderText()
    {
        var decision = CrmProspectingExtractionPollPolicy.Evaluate(
            new(CrmProspectingExtractionProviderStatus.Failed, null, "  BAD / KEY\nsecret  ", "provider echoed arbitrary payload"),
            Now, 0);

        Assert.Equal("badkeysecret", decision.FailureCode);
        Assert.Equal("The provider reported that the extraction failed.", decision.SafeFailureSummary);
        Assert.DoesNotContain("arbitrary payload", decision.SafeFailureSummary);
    }

    [Fact]
    public void Evaluate_RejectsNegativeAttemptCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrmProspectingExtractionPollPolicy.Evaluate(
                new(CrmProspectingExtractionProviderStatus.Pending, null, null, null), Now, -1));
    }
}
