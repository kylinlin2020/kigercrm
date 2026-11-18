using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Timezone-aware recurrence boundaries: intervals, month-end clamping, leap years and DST.</summary>
public sealed class CrmProspectingScheduleRecurrenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Daily_ShouldHonorIntervalAndStart()
    {
        var next = CrmProspectingScheduleRecurrence.NextOccurrence(
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), Start, null, "UTC",
            CrmProspectingScheduleFrequency.Daily, 3, 9 * 60, null, null);
        Assert.Equal(new DateTimeOffset(2026, 1, 4, 9, 0, 0, TimeSpan.Zero), next);
        Assert.Equal(Start, CrmProspectingScheduleRecurrence.NextOccurrence(
            Start.AddTicks(-1), Start, null, "UTC",
            CrmProspectingScheduleFrequency.Daily, 1, 9 * 60, null, null));
    }

    [Fact]
    public void Weekly_ShouldTargetTheConfiguredWeekday()
    {
        // 2026-01-05 is a Monday; Wednesday is 3 in DayOfWeek.
        var next = CrmProspectingScheduleRecurrence.NextOccurrence(
            new DateTimeOffset(2026, 1, 7, 0, 0, 0, TimeSpan.Zero), Start, null, "UTC",
            CrmProspectingScheduleFrequency.Weekly, 1, 9 * 60, 3, null);
        Assert.Equal(new DateTimeOffset(2026, 1, 7, 9, 0, 0, TimeSpan.Zero), next);
        var following = CrmProspectingScheduleRecurrence.NextOccurrence(
            next!.Value, Start, null, "UTC",
            CrmProspectingScheduleFrequency.Weekly, 1, 9 * 60, 3, null);
        Assert.Equal(new DateTimeOffset(2026, 1, 14, 9, 0, 0, TimeSpan.Zero), following);
    }

    [Fact]
    public void Monthly_ShouldClampDay31AndSupportLastDay()
    {
        var jan31 = new DateTimeOffset(2026, 1, 31, 9, 0, 0, TimeSpan.Zero);
        var feb = CrmProspectingScheduleRecurrence.NextOccurrence(
            jan31, jan31, null, "UTC",
            CrmProspectingScheduleFrequency.Monthly, 1, 9 * 60, null, 31);
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 9, 0, 0, TimeSpan.Zero), feb);
        var march = CrmProspectingScheduleRecurrence.NextOccurrence(
            feb!.Value, jan31, null, "UTC",
            CrmProspectingScheduleFrequency.Monthly, 1, 9 * 60, null, 31);
        Assert.Equal(new DateTimeOffset(2026, 3, 31, 9, 0, 0, TimeSpan.Zero), march);
        var lastDay = CrmProspectingScheduleRecurrence.NextOccurrence(
            new DateTimeOffset(2026, 1, 30, 0, 0, 0, TimeSpan.Zero), jan31, null, "UTC",
            CrmProspectingScheduleFrequency.Monthly, 1, 9 * 60, null, -1);
        Assert.Equal(jan31, lastDay);
    }

    [Fact]
    public void Monthly_ShouldRespectLeapYear()
    {
        var leapStart = new DateTimeOffset(2028, 1, 29, 9, 0, 0, TimeSpan.Zero);
        var next = CrmProspectingScheduleRecurrence.NextOccurrence(
            leapStart.AddTicks(-1), leapStart, null, "UTC",
            CrmProspectingScheduleFrequency.Monthly, 1, 9 * 60, null, 29);
        Assert.Equal(leapStart, next);
        var following = CrmProspectingScheduleRecurrence.NextOccurrence(
            leapStart, leapStart, null, "UTC",
            CrmProspectingScheduleFrequency.Monthly, 1, 9 * 60, null, 29);
        Assert.Equal(new DateTimeOffset(2028, 2, 29, 9, 0, 0, TimeSpan.Zero), following);
    }

    [Fact]
    public void Dst_ShouldAdvancePastGapAndPickEarlierInstantOnFold()
    {
        // New York springs forward on 2026-03-08 at 02:00; 02:30 local does not exist.
        var gap = CrmProspectingScheduleRecurrence.NextOccurrence(
            new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 2, 30, 0, TimeSpan.Zero), null, "America/New_York",
            CrmProspectingScheduleFrequency.Daily, 1, 2 * 60 + 30, null, null);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), gap);

        // 2026-11-01 01:30 local occurs twice; the earlier UTC instant is the deterministic choice.
        var fold = CrmProspectingScheduleRecurrence.NextOccurrence(
            new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.Zero), null, "America/New_York",
            CrmProspectingScheduleFrequency.Daily, 1, 1 * 60 + 30, null, null);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), fold);
    }

    [Fact]
    public void ShouldStopAtEndDateAndRejectInvalidConfiguration()
    {
        Assert.Null(CrmProspectingScheduleRecurrence.NextOccurrence(
            new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero), Start,
            new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero), "UTC",
            CrmProspectingScheduleFrequency.Daily, 1, 9 * 60, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrmProspectingScheduleRecurrence.NextOccurrence(
            Start, Start, null, "UTC", CrmProspectingScheduleFrequency.Daily, 0, 9 * 60, null, null));
        Assert.Throws<ArgumentException>(() => CrmProspectingScheduleRecurrence.NextOccurrence(
            Start, Start, null, "Not/AZone", CrmProspectingScheduleFrequency.Daily, 1, 9 * 60, null, null));
    }
}
