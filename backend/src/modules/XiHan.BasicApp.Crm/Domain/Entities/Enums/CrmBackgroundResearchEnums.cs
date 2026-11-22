namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

/// <summary>Local CRM source that contributed a background-research fact.</summary>
public enum CrmResearchSource
{
    CustomerProfile = 0,
    OpenOpportunity = 1,
    SourceLead = 2,
}

/// <summary>Lifecycle of a background research report.</summary>
public enum CrmResearchReportStatus
{
    Current = 0,
    Superseded = 1,
}

/// <summary>Review state of a proposed market opportunity.</summary>
public enum CrmMarketOpportunityStatus
{
    Proposed = 0,
    Confirmed = 1,
    Dismissed = 2,
}
