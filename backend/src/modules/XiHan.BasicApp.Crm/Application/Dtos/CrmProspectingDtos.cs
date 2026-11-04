namespace XiHan.BasicApp.Crm.Application.Dtos;

/// <summary>Campaign creation and update input.</summary>
public sealed class CrmProspectingCampaignInputDto
{
    public long? BasicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Objective { get; set; }
    public string? ProductScope { get; set; }
    public string? TargetMarkets { get; set; }
    public string? IdealCustomerProfile { get; set; }
    public long? OwnerUserId { get; set; }
}

/// <summary>Campaign detail response.</summary>
public sealed class CrmProspectingCampaignDto
{
    public long BasicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Objective { get; set; }
    public string? ProductScope { get; set; }
    public string? TargetMarkets { get; set; }
    public string? IdealCustomerProfile { get; set; }
    public int Status { get; set; }
    public long? OwnerUserId { get; set; }
    public int? ActiveStrategyVersion { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}

/// <summary>Creates a new immutable strategy version.</summary>
public sealed class CrmProspectingStrategyInputDto
{
    public long CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SearchObjective { get; set; } = string.Empty;
    public string ProviderPlanJson { get; set; } = "[]";
    public string? ExcludedDomainsJson { get; set; }
}

/// <summary>Strategy version response.</summary>
public sealed class CrmProspectingStrategyDto
{
    public long BasicId { get; set; }
    public long CampaignId { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SearchObjective { get; set; } = string.Empty;
    public string ProviderPlanJson { get; set; } = "[]";
    public string? ExcludedDomainsJson { get; set; }
    public int ApprovalStatus { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}

/// <summary>Decision for a pending strategy version.</summary>
public sealed class CrmProspectingStrategyDecisionDto
{
    public long StrategyId { get; set; }
    public bool Approve { get; set; }
}
