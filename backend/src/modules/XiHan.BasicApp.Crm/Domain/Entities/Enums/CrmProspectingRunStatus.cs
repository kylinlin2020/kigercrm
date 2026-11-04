namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

/// <summary>Execution lifecycle; queued is not equivalent to completed.</summary>
public enum CrmProspectingRunStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    PartiallySucceeded = 3,
    Failed = 4,
    CancelRequested = 5,
    Cancelled = 6
}
