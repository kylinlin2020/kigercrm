namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Pure transition policy for provider-side asynchronous extraction jobs.
/// Persistence, tenant scoping, leases and retries belong to the application/infrastructure layers.
/// </summary>
public static class CrmProspectingExtractionStateMachine
{
    /// <summary>
    /// Returns whether a persisted state may move to the requested state.
    /// Repeated Pending/Running observations and repeated Succeeded while fetching are idempotent.
    /// Terminal states never regress.
    /// </summary>
    public static bool CanTransition(
        CrmProspectingExtractionStatus current,
        CrmProspectingExtractionStatus next)
    {
        if (!Enum.IsDefined(current) || !Enum.IsDefined(next))
            return false;

        if (current == next)
            return current is CrmProspectingExtractionStatus.Pending
                or CrmProspectingExtractionStatus.Running;

        return current switch
        {
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

            // Succeeded means the provider finished computing; result pages may still need fetching.
            CrmProspectingExtractionStatus.Succeeded =>
                next is CrmProspectingExtractionStatus.Failed
                    or CrmProspectingExtractionStatus.Expired,

            CrmProspectingExtractionStatus.NeedsReview =>
                next is CrmProspectingExtractionStatus.Pending
                    or CrmProspectingExtractionStatus.Running
                    or CrmProspectingExtractionStatus.Failed
                    or CrmProspectingExtractionStatus.Expired
                    or CrmProspectingExtractionStatus.Cancelled,

            CrmProspectingExtractionStatus.Failed
                or CrmProspectingExtractionStatus.Expired
                or CrmProspectingExtractionStatus.Cancelled => false,

            _ => false
        };
    }

    /// <summary>Only a fully persisted final page may mark the extraction Completed.</summary>
    public static bool CanCompleteResults(
        CrmProspectingExtractionStatus current,
        bool isLastPage,
        bool isComplete,
        int failureCount)
        => current == CrmProspectingExtractionStatus.Succeeded
            && isLastPage
            && isComplete
            && failureCount == 0;
}
