namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

/// <summary>Approval lifecycle of a recurring prospecting schedule.</summary>
public enum CrmProspectingScheduleStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Paused = 3,
    Cancelled = 4,

    /// <summary>Approval was explicitly rejected; the schedule never becomes due.</summary>
    Rejected = 5,
}
