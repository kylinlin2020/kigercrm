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

/// <summary>Idempotent request to queue a prospecting run for the campaign's active strategy.</summary>
public sealed class CrmProspectingRunStartDto
{
    public long CampaignId { get; set; }
    public string RequestKey { get; set; } = string.Empty;
}

/// <summary>Execution state and counters; queue acceptance does not mean completion.</summary>
public sealed class CrmProspectingRunDto
{
    public long BasicId { get; set; }
    public long CampaignId { get; set; }
    public long StrategyId { get; set; }
    public int StrategyVersion { get; set; }
    public int Status { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int RawRecordCount { get; set; }
    public int CandidateCount { get; set; }
    public int FailureCount { get; set; }
    public string? FailureSummary { get; set; }
}
