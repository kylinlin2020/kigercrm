namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

/// <summary>Approval lifecycle for an immutable prospecting strategy version.</summary>
public enum CrmProspectingStrategyApprovalStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Revoked = 4
}
