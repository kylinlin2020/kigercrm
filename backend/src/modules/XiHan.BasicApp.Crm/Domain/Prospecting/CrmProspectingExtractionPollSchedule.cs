namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Produces bounded retry times for remote extraction polling. Provider retry hints are untrusted:
/// they may not force immediate hot loops or defer a tenant job indefinitely.
/// </summary>
public static class CrmProspectingExtractionPollSchedule
{
    public static readonly TimeSpan MinimumDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(15);

    public static DateTimeOffset GetNextPollAtUtc(
        DateTimeOffset nowUtc,
        int attemptCount,
        DateTimeOffset? providerRetryAtUtc = null,
        TimeSpan? providerPollAfter = null)
    {
        if (attemptCount < 0)
            throw new ArgumentOutOfRangeException(nameof(attemptCount));

        // The persisted column and method contract are UTC; normalize caller offsets before
        // applying provider hints so equivalent instants produce identical timestamps.
        nowUtc = nowUtc.ToUniversalTime();

        var exponent = Math.Min(attemptCount, 20);
        var seconds = Math.Min(
            MaximumDelay.TotalSeconds,
            MinimumDelay.TotalSeconds * Math.Pow(2, exponent));
        var delay = TimeSpan.FromSeconds(seconds);

        if (providerPollAfter is { } pollAfter)
        {
            var boundedHint = pollAfter < MinimumDelay
                ? MinimumDelay
                : pollAfter > MaximumDelay ? MaximumDelay : pollAfter;
            if (boundedHint > delay) delay = boundedHint;
        }

        var scheduledAt = nowUtc + delay;
        if (providerRetryAtUtc is { } retryAt)
        {
            var boundedRetryAt = retryAt < nowUtc + MinimumDelay
                ? nowUtc + MinimumDelay
                : retryAt > nowUtc + MaximumDelay ? nowUtc + MaximumDelay : retryAt;
            if (boundedRetryAt > scheduledAt) scheduledAt = boundedRetryAt;
        }

        return scheduledAt;
    }
}
