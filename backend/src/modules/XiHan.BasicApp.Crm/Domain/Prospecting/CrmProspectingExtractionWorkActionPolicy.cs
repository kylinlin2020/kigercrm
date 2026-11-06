namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Selects the only safe next remote operation from a persisted extraction lifecycle state.
/// This policy does not perform I/O or mutate state; callers must still hold the exact live job lease.
/// </summary>
public static class CrmProspectingExtractionWorkActionPolicy
{
    public static CrmProspectingExtractionWorkAction Evaluate(
        CrmProspectingExtractionStatus status,
        string? providerExtractionId)
    {
        if (!Enum.IsDefined(status))
            return new(CrmProspectingExtractionWorkAction.None, "unknown_local_status");

        var hasRemoteIdentity = !string.IsNullOrWhiteSpace(providerExtractionId);

        return status switch
        {
            // Reuse the already-persisted submission key on every retry. The async registry
            // requires provider-documented idempotency, so this action never authorizes a new key.
            CrmProspectingExtractionStatus.Submitting when !hasRemoteIdentity =>
                new(CrmProspectingExtractionWorkAction.Submit, null),
            CrmProspectingExtractionStatus.Submitting =>
                new(CrmProspectingExtractionWorkAction.None, "submitting_has_remote_identity"),

            CrmProspectingExtractionStatus.Pending or CrmProspectingExtractionStatus.Running
                when hasRemoteIdentity =>
                new(CrmProspectingExtractionWorkAction.Poll, null),
            CrmProspectingExtractionStatus.Pending or CrmProspectingExtractionStatus.Running =>
                new(CrmProspectingExtractionWorkAction.None, "poll_requires_remote_identity"),

            CrmProspectingExtractionStatus.Succeeded or CrmProspectingExtractionStatus.FetchingResults
                when hasRemoteIdentity =>
                new(CrmProspectingExtractionWorkAction.FetchResults, null),
            CrmProspectingExtractionStatus.Succeeded or CrmProspectingExtractionStatus.FetchingResults =>
                new(CrmProspectingExtractionWorkAction.None, "fetch_requires_remote_identity"),

            // NeedsReview is deliberately not an automatic action. Terminal states are inert.
            _ => new(CrmProspectingExtractionWorkAction.None, null)
        };
    }
}

/// <summary>One safe remote operation selected for a leased extraction job.</summary>
public enum CrmProspectingExtractionWorkAction
{
    None = 0,
    Submit = 1,
    Poll = 2,
    FetchResults = 3
}

/// <summary>Pure work-selection result with a stable reason code when an invalid state is blocked.</summary>
public sealed record CrmProspectingExtractionWorkActionDecision(
    CrmProspectingExtractionWorkAction Action,
    string? BlockReason);
