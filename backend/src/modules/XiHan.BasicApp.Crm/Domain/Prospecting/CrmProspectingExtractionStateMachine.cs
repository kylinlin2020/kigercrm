namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Pure transition policy for durable extraction jobs. Provider remote status and local result-fetch
/// lifecycle share one enum so persistence can represent the full state without treating submission as success.
/// </summary>
public static class CrmProspectingExtractionStateMachine
{
    public static bool CanTransition(
        CrmProspectingExtractionStatus current,
        CrmProspectingExtractionStatus next)
    {
        if (!Enum.IsDefined(current) || !Enum.IsDefined(next))
            return false;

        if (current == next)
            return current is CrmProspectingExtractionStatus.Pending
                or CrmProspectingExtractionStatus.Running
                or CrmProspectingExtractionStatus.FetchingResults;

        return current switch
        {
            // Acknowledgement is a persistence operation, not a generic state transition:
            // only TryAcknowledgeSubmissionAsync may move Submitting to Pending after validating
            // and storing the provider's opaque extraction identity under the live lease.
            CrmProspectingExtractionStatus.Submitting =>
                next is CrmProspectingExtractionStatus.NeedsReview
                    or CrmProspectingExtractionStatus.Failed,

            CrmProspectingExtractionStatus.Pending =>
                next is CrmProspectingExtractionStatus.Running
                    or CrmProspectingExtractionStatus.Succeeded
                    or CrmProspectingExtractionStatus.Failed
                    or CrmProspectingExtractionStatus.Expired
                    or CrmProspectingExtractionStatus.Cancelled
                    or CrmProspectingExtractionStatus.NeedsReview,

            CrmProspectingExtractionStatus.Running =>
                next is CrmProspectingExtractionStatus.Succeeded
                    or CrmProspectingExtractionStatus.Failed
                    or CrmProspectingExtractionStatus.Expired
                    or CrmProspectingExtractionStatus.Cancelled
                    or CrmProspectingExtractionStatus.NeedsReview,

            CrmProspectingExtractionStatus.Succeeded =>
                next is CrmProspectingExtractionStatus.FetchingResults
                    or CrmProspectingExtractionStatus.Failed
                    or CrmProspectingExtractionStatus.Expired
                    or CrmProspectingExtractionStatus.Cancelled,

            CrmProspectingExtractionStatus.FetchingResults =>
                next is CrmProspectingExtractionStatus.Completed
                    or CrmProspectingExtractionStatus.Failed
                    or CrmProspectingExtractionStatus.Expired
                    or CrmProspectingExtractionStatus.Cancelled,

            CrmProspectingExtractionStatus.NeedsReview =>
                next is CrmProspectingExtractionStatus.Pending
                    or CrmProspectingExtractionStatus.Running
                    or CrmProspectingExtractionStatus.Failed
                    or CrmProspectingExtractionStatus.Expired
                    or CrmProspectingExtractionStatus.Cancelled,

            CrmProspectingExtractionStatus.Failed
                or CrmProspectingExtractionStatus.Expired
                or CrmProspectingExtractionStatus.Cancelled
                or CrmProspectingExtractionStatus.Completed => false,

            _ => false
        };
    }

    /// <summary>Live remote lifecycle states require a persisted provider-side extraction identity.</summary>
    public static bool RequiresProviderExtractionId(CrmProspectingExtractionStatus status)
        => status is CrmProspectingExtractionStatus.Pending
            or CrmProspectingExtractionStatus.Running
            or CrmProspectingExtractionStatus.Succeeded
            or CrmProspectingExtractionStatus.FetchingResults
            or CrmProspectingExtractionStatus.Completed;

    /// <summary>Only a fully persisted final page may complete a job currently fetching results.</summary>
    public static bool CanCompleteResults(
        CrmProspectingExtractionStatus current,
        bool isLastPage,
        bool isComplete,
        int failureCount)
        => current == CrmProspectingExtractionStatus.FetchingResults
            && isLastPage
            && isComplete
            && failureCount == 0;
}
