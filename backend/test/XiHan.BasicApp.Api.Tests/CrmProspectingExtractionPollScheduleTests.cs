using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingExtractionPollScheduleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 80)]
    public void GetNextPollAtUtc_UsesBoundedExponentialBackoff(int attempt, int expectedSeconds)
    {
        var result = CrmProspectingExtractionPollSchedule.GetNextPollAtUtc(Now, attempt);

        Assert.Equal(Now.AddSeconds(expectedSeconds), result);
    }

    [Fact]
    public void GetNextPollAtUtc_CapsLargeAttemptCount()
    {
        var result = CrmProspectingExtractionPollSchedule.GetNextPollAtUtc(Now, int.MaxValue);

        Assert.Equal(Now + CrmProspectingExtractionPollSchedule.MaximumDelay, result);
    }

    [Fact]
    public void GetNextPollAtUtc_RespectsProviderPollAfterWithinBounds()
    {
        var result = CrmProspectingExtractionPollSchedule.GetNextPollAtUtc(
            Now, 0, providerPollAfter: TimeSpan.FromMinutes(2));

        Assert.Equal(Now.AddMinutes(2), result);
    }

    [Fact]
    public void GetNextPollAtUtc_ClampsProviderHintsToMinimumAndMaximum()
    {
        var tooSoon = CrmProspectingExtractionPollSchedule.GetNextPollAtUtc(
            Now, 0, providerPollAfter: TimeSpan.Zero);
        var tooLate = CrmProspectingExtractionPollSchedule.GetNextPollAtUtc(
            Now, 0, providerPollAfter: TimeSpan.FromDays(30));

        Assert.Equal(Now + CrmProspectingExtractionPollSchedule.MinimumDelay, tooSoon);
        Assert.Equal(Now + CrmProspectingExtractionPollSchedule.MaximumDelay, tooLate);
    }

    [Fact]
    public void GetNextPollAtUtc_ClampsProviderRetryTimestamp()
    {
        var past = CrmProspectingExtractionPollSchedule.GetNextPollAtUtc(
            Now, 0, providerRetryAtUtc: Now.AddMinutes(-1));
        var distant = CrmProspectingExtractionPollSchedule.GetNextPollAtUtc(
            Now, 0, providerRetryAtUtc: Now.AddDays(3));

        Assert.Equal(Now + CrmProspectingExtractionPollSchedule.MinimumDelay, past);
        Assert.Equal(Now + CrmProspectingExtractionPollSchedule.MaximumDelay, distant);
    }

    [Fact]
    public void GetNextPollAtUtc_RejectsNegativeAttemptCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrmProspectingExtractionPollSchedule.GetNextPollAtUtc(Now, -1));
    }

    [Fact]
    public void GetNextPollAtUtc_NormalizesNonUtcInputToUtc()
    {
        var localOffsetNow = new DateTimeOffset(2026, 11, 6, 14, 0, 0, TimeSpan.FromHours(2));

        var result = CrmProspectingExtractionPollSchedule.GetNextPollAtUtc(localOffsetNow, 0);

        Assert.Equal(Now.AddSeconds(5), result);
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

}
