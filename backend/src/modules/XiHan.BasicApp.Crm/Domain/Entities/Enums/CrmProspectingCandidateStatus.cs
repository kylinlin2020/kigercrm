namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

/// <summary>Human-review lifecycle for a prospecting candidate.</summary>
public enum CrmProspectingCandidateStatus
{
    PendingReview = 0,
    Duplicate = 1,
    Approved = 2,
    Rejected = 3,
    Converted = 4
}
