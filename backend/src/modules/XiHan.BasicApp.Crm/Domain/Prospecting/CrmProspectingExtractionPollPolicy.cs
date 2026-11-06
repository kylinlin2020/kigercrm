namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Converts an untrusted provider poll response into a safe local lifecycle decision.
/// Unknown numeric enum values fail closed to NeedsReview and are never interpreted as success.
/// </summary>
public static class CrmProspectingExtractionPollPolicy
{
    public static CrmProspectingExtractionPollDecision Evaluate(
        CrmProspectingExtractionPollResult? poll,
        DateTimeOffset nowUtc,
        int currentAttemptCount)
    {
        if (currentAttemptCount < 0)
            throw new ArgumentOutOfRangeException(nameof(currentAttemptCount));

        if (poll is null || !Enum.IsDefined(poll.Status))
        {
            return new(
                CrmProspectingExtractionStatus.NeedsReview,
                null,
                "unknown_provider_status",
                "The provider returned an unknown extraction status; manual review is required.");
        }

        switch (poll.Status)
        {
            case CrmProspectingExtractionProviderStatus.Pending:
                return Schedule(CrmProspectingExtractionStatus.Pending, poll, nowUtc, currentAttemptCount);
            case CrmProspectingExtractionProviderStatus.Running:
                return Schedule(CrmProspectingExtractionStatus.Running, poll, nowUtc, currentAttemptCount);
            case CrmProspectingExtractionProviderStatus.Succeeded:
                return new(CrmProspectingExtractionStatus.Succeeded, null, null, null);
            case CrmProspectingExtractionProviderStatus.Failed:
                return new(
                    CrmProspectingExtractionStatus.Failed,
                    null,
                    NormalizeFailureCode(poll.FailureCode),
                    "The provider reported that the extraction failed.");
            case CrmProspectingExtractionProviderStatus.Expired:
                return new(
                    CrmProspectingExtractionStatus.Expired,
                    null,
                    "provider_extraction_expired",
                    "The provider reported that the extraction expired.");
            case CrmProspectingExtractionProviderStatus.Cancelled:
                return new(
                    CrmProspectingExtractionStatus.Cancelled,
                    null,
                    "provider_extraction_cancelled",
                    "The provider reported that the extraction was cancelled.");
            case CrmProspectingExtractionProviderStatus.NeedsReview:
            default:
                return new(
                    CrmProspectingExtractionStatus.NeedsReview,
                    null,
                    "provider_requires_review",
                    "The provider status requires manual review.");
        }
    }

    /// <summary>
    /// Evaluates a remote response against the persisted local state. This overload prevents
    /// non-monotonic provider regressions (for example Running back to Pending) from reaching
    /// the repository transition boundary as an invalid write.
    /// </summary>
    public static CrmProspectingExtractionPollDecision EvaluateForCurrentState(
        CrmProspectingExtractionStatus currentStatus,
        CrmProspectingExtractionPollResult? poll,
        DateTimeOffset nowUtc,
        int currentAttemptCount)
    {
        if (currentStatus is not (CrmProspectingExtractionStatus.Pending or CrmProspectingExtractionStatus.Running))
        {
            return new(
                CrmProspectingExtractionStatus.NeedsReview,
                null,
                "invalid_local_poll_state",
                "The extraction is not in a pollable local state; manual review is required.");
        }

        var decision = Evaluate(poll, nowUtc, currentAttemptCount);
        if (decision.NextStatus == CrmProspectingExtractionStatus.NeedsReview)
            return decision;

        if (!CrmProspectingExtractionStateMachine.CanTransition(currentStatus, decision.NextStatus))
        {
            return new(
                CrmProspectingExtractionStatus.NeedsReview,
                null,
                "provider_status_regression",
                "The provider returned a status inconsistent with the persisted extraction lifecycle; manual review is required.");
        }

        return decision;
    }

    private static CrmProspectingExtractionPollDecision Schedule(
        CrmProspectingExtractionStatus status,
        CrmProspectingExtractionPollResult poll,
        DateTimeOffset nowUtc,
        int currentAttemptCount)
        => new(
            status,
            CrmProspectingExtractionPollSchedule.GetNextPollAtUtc(
                nowUtc, currentAttemptCount, poll.RetryAtUtc),
            null,
            null);

    private static string NormalizeFailureCode(string? failureCode)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
            return "provider_extraction_failed";

        var normalized = new string(failureCode
            .Trim()
            .ToLowerInvariant()
            .Where(character => character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_' or '-')
            .Take(80)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized)
            ? "provider_extraction_failed"
            : normalized;
    }
}

/// <summary>Validated local transition and optional next poll schedule.</summary>
public sealed record CrmProspectingExtractionPollDecision(
    CrmProspectingExtractionStatus NextStatus,
    DateTimeOffset? NextPollAtUtc,
    string? FailureCode,
    string? SafeFailureSummary);
