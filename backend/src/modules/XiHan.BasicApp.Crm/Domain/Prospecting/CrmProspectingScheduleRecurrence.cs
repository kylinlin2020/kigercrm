using XiHan.BasicApp.Crm.Domain.Entities.Enums;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Deterministic, timezone-aware recurrence. All date math happens in the schedule's local wall
/// clock, then the local candidate is converted to a single UTC instant with explicit handling of
/// DST gaps (advance to the first valid minute) and folds (choose the earlier instant). Monthly day
/// 31 clamps to the last day of shorter months and -1 always means the last day (leap years included).
/// </summary>
public static class CrmProspectingScheduleRecurrence
{
    private const int MaximumIterations = 20_000;
    private const int MaximumDstGapMinutes = 180;

    public static DateTimeOffset? NextOccurrence(
        DateTimeOffset notBeforeUtc,
        DateTimeOffset startDateUtc,
        DateTimeOffset? endDateUtc,
        string timeZoneId,
        CrmProspectingScheduleFrequency frequency,
        int interval,
        int timeOfDayMinutes,
        int? dayOfWeek,
        int? dayOfMonth)
    {
        if (interval is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(interval));
        if (timeOfDayMinutes is < 0 or > 1439) throw new ArgumentOutOfRangeException(nameof(timeOfDayMinutes));
        if (frequency == CrmProspectingScheduleFrequency.Weekly && dayOfWeek is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(dayOfWeek));
        if (frequency == CrmProspectingScheduleFrequency.Monthly && dayOfMonth is not (-1 or >= 1 and <= 31))
            throw new ArgumentOutOfRangeException(nameof(dayOfMonth));
        if (endDateUtc.HasValue && endDateUtc.Value < startDateUtc)
            throw new ArgumentOutOfRangeException(nameof(endDateUtc));

        var timeZone = ResolveTimeZone(timeZoneId);
        var startLocal = TimeZoneInfo.ConvertTime(startDateUtc, timeZone).DateTime;
        var notBeforeLocal = TimeZoneInfo.ConvertTime(notBeforeUtc, timeZone).DateTime;
        var local = FirstCandidate(startLocal.Date, notBeforeLocal, frequency, interval, timeOfDayMinutes, dayOfWeek, dayOfMonth);

        for (var iteration = 0; iteration < MaximumIterations; iteration++)
        {
            var utc = ToUtc(local, timeZone);
            if (utc >= startDateUtc && utc > notBeforeUtc)
            {
                if (endDateUtc.HasValue && utc > endDateUtc.Value) return null;
                return utc;
            }
            local = NextLocalDate(local.Date, frequency, interval, dayOfWeek, dayOfMonth)
                .AddMinutes(timeOfDayMinutes);
        }
        throw new InvalidOperationException("The recurrence search exceeded its bounded iteration limit.");
    }

    private static DateTime FirstCandidate(
        DateTime anchorDate,
        DateTime notBeforeLocal,
        CrmProspectingScheduleFrequency frequency,
        int interval,
        int timeOfDayMinutes,
        int? dayOfWeek,
        int? dayOfMonth)
    {
        var candidateDate = frequency switch
        {
            CrmProspectingScheduleFrequency.Daily => DailyCandidate(anchorDate, notBeforeLocal.Date, interval),
            CrmProspectingScheduleFrequency.Weekly => WeeklyCandidate(anchorDate, notBeforeLocal.Date, interval, dayOfWeek!.Value),
            CrmProspectingScheduleFrequency.Monthly => MonthlyCandidate(anchorDate, notBeforeLocal.Date, interval, dayOfMonth!.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
        };
        return candidateDate.AddMinutes(timeOfDayMinutes);
    }

    private static DateTime DailyCandidate(DateTime anchorDate, DateTime targetDate, int interval)
    {
        var days = Math.Max(0, (targetDate - anchorDate).Days);
        return anchorDate.AddDays(days - (days % interval));
    }

    private static DateTime WeeklyCandidate(DateTime anchorDate, DateTime targetDate, int interval, int desiredDayOfWeek)
    {
        var daysUntilDesired = (desiredDayOfWeek - (int)anchorDate.DayOfWeek + 7) % 7;
        var first = anchorDate.AddDays(daysUntilDesired);
        var daysAfterFirst = (targetDate - first).Days;
        if (daysAfterFirst <= 0) return first;
        var wholePeriods = daysAfterFirst / (7 * interval);
        return first.AddDays(wholePeriods * 7 * interval);
    }

    private static DateTime MonthlyCandidate(DateTime anchorDate, DateTime targetDate, int interval, int configuredDay)
    {
        var monthsAfterAnchor = ((targetDate.Year - anchorDate.Year) * 12) + targetDate.Month - anchorDate.Month;
        if (monthsAfterAnchor <= 0) monthsAfterAnchor = 0;
        var months = monthsAfterAnchor - (monthsAfterAnchor % interval);
        var month = new DateTime(anchorDate.Year, anchorDate.Month, 1).AddMonths(months);
        return month.AddDays(ResolveMonthDay(month.Year, month.Month, configuredDay) - 1);
    }

    private static DateTime NextLocalDate(
        DateTime currentDate,
        CrmProspectingScheduleFrequency frequency,
        int interval,
        int? dayOfWeek,
        int? dayOfMonth) => frequency switch
    {
        CrmProspectingScheduleFrequency.Daily => currentDate.AddDays(interval),
        CrmProspectingScheduleFrequency.Weekly => currentDate.AddDays(7 * interval),
        CrmProspectingScheduleFrequency.Monthly => NextMonthlyDate(currentDate, interval, dayOfMonth!.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
    };

    private static DateTime NextMonthlyDate(DateTime currentDate, int interval, int configuredDay)
    {
        var month = new DateTime(currentDate.Year, currentDate.Month, 1).AddMonths(interval);
        return month.AddDays(ResolveMonthDay(month.Year, month.Month, configuredDay) - 1);
    }

    private static int ResolveMonthDay(int year, int month, int configuredDay)
        => configuredDay == -1
            ? DateTime.DaysInMonth(year, month)
            : Math.Min(configuredDay, DateTime.DaysInMonth(year, month));

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        var candidate = local;
        for (var minute = 0; minute < MaximumDstGapMinutes && timeZone.IsInvalidTime(candidate); minute++)
            candidate = candidate.AddMinutes(1);
        if (timeZone.IsAmbiguousTime(candidate))
        {
            // The earlier UTC instant of a fold is the deterministic choice.
            var offset = timeZone.GetAmbiguousTimeOffsets(candidate).Max();
            return new DateTimeOffset(candidate, offset).ToUniversalTime();
        }
        return new DateTimeOffset(candidate, timeZone.GetUtcOffset(candidate)).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId) && windowsId is not null)
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId) && ianaId is not null)
                return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            throw new ArgumentException($"Unknown time zone '{timeZoneId}'.", nameof(timeZoneId));
        }
    }
}
