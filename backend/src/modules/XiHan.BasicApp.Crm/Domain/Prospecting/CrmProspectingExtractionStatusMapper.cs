namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Maps normalized remote provider states into the local durable extraction lifecycle.</summary>
public static class CrmProspectingExtractionStatusMapper
{
    private static readonly TimeSpan MinimumPollDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumPollDelay = TimeSpan.FromMinutes(30);

    public static CrmProspectingExtractionStatus Map(
        CrmProspectingExtractionProviderStatus providerStatus)
        => providerStatus switch
        {
            CrmProspectingExtractionProviderStatus.Pending => CrmProspectingExtractionStatus.Pending,
            CrmProspectingExtractionProviderStatus.Running => CrmProspectingExtractionStatus.Running,
            CrmProspectingExtractionProviderStatus.Succeeded => CrmProspectingExtractionStatus.Succeeded,
            CrmProspectingExtractionProviderStatus.Failed => CrmProspectingExtractionStatus.Failed,
            CrmProspectingExtractionProviderStatus.Expired => CrmProspectingExtractionStatus.Expired,
            CrmProspectingExtractionProviderStatus.Cancelled => CrmProspectingExtractionStatus.Cancelled,
            CrmProspectingExtractionProviderStatus.NeedsReview => CrmProspectingExtractionStatus.NeedsReview,
            _ => CrmProspectingExtractionStatus.NeedsReview
        };

    /// <summary>
    /// Clamp provider retry hints to a safe scheduling window. Provider timestamps are advisory;
    /// the caller must still enforce the persisted extraction expiry.
    /// </summary>

    /// <summary>
    /// Computes bounded exponential polling backoff when the provider supplies no retry hint.
    /// Attempt numbers are one-based; delay grows from five seconds and caps at thirty minutes.
    /// </summary>
    public static TimeSpan GetDefaultPollDelay(int pollAttemptCount)
    {
        if (pollAttemptCount < 1)
            throw new ArgumentOutOfRangeException(nameof(pollAttemptCount));

        var delay = MinimumPollDelay;
        for (var attempt = 1; attempt < pollAttemptCount && delay < MaximumPollDelay; attempt++)
        {
            delay = delay >= MaximumPollDelay - delay
                ? MaximumPollDelay
                : delay + delay;
        }

        return delay > MaximumPollDelay ? MaximumPollDelay : delay;
    }

    public static DateTimeOffset GetDefaultRetryAtUtc(int pollAttemptCount, DateTimeOffset nowUtc)
        => nowUtc.ToUniversalTime().Add(GetDefaultPollDelay(pollAttemptCount));

    public static DateTimeOffset NormalizeRetryAtUtc(
        DateTimeOffset? providerRetryAtUtc,
        DateTimeOffset nowUtc)
    {
        var minimum = nowUtc.Add(MinimumPollDelay);
        var maximum = nowUtc.Add(MaximumPollDelay);
        if (providerRetryAtUtc is null) return nowUtc.Add(TimeSpan.FromMinutes(1));
        var requested = providerRetryAtUtc.Value.ToUniversalTime();
        if (requested < minimum) return minimum;
        if (requested > maximum) return maximum;
        return requested;
    }
}
