using XiHan.BasicApp.Core.Dtos;
using XiHan.BasicApp.Crm.Domain.Prospecting;

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


/// <summary>Candidate row for the human-review queue.</summary>
public sealed class CrmProspectingCandidateDto
{
    public long BasicId { get; set; }
    public long RunId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? Title { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? BusinessEmail { get; set; }
    public string? Website { get; set; }
    public string? Summary { get; set; }
    public int Status { get; set; }
    public string? DuplicateReason { get; set; }
    public long? ConvertedLeadId { get; set; }
    public DateTimeOffset? ShortlistedAt { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}

/// <summary>Human decision for a prospecting candidate. Duplicate override must be explicit.</summary>
public sealed class CrmProspectingCandidateReviewDto
{
    public long CandidateId { get; set; }
    public bool Approve { get; set; }
    public bool OverrideDuplicate { get; set; }
}

/// <summary>Explicit id list for one bulk candidate operation.</summary>
public sealed class CrmProspectingCandidateBulkDto
{
    public IReadOnlyList<long> CandidateIds { get; set; } = [];
}

/// <summary>Bulk review: one approve/reject decision applied item by item under the single-item state machine.</summary>
public sealed class CrmProspectingCandidateBulkReviewDto
{
    public IReadOnlyList<long> CandidateIds { get; set; } = [];
    public bool Approve { get; set; }
    public bool OverrideDuplicate { get; set; }
}

/// <summary>One item's honest outcome of a bulk operation: committed, idempotently skipped or failed with a reason.</summary>
public sealed class CrmProspectingBulkItemResultDto
{
    public long CandidateId { get; set; }
    /// <summary><c>CrmProspectingBulkOutcome</c> as a wire number.</summary>
    public int Outcome { get; set; }
    public string? Reason { get; set; }
    /// <summary>Lead reference for a conversion that succeeded or was skipped as already converted.</summary>
    public long? LeadId { get; set; }
}

/// <summary>Ordered per-item results of a bulk operation; counts are derived from <see cref="Items"/>.</summary>
public sealed class CrmProspectingBulkResultDto
{
    public IReadOnlyList<CrmProspectingBulkItemResultDto> Items { get; set; } = [];
}


/// <summary>Field-level provenance shown to a human reviewer.</summary>
public sealed class CrmProspectingEvidenceDto
{
    public long BasicId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string? ProviderRecordId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string? FieldValueHash { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string EvidenceType { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = string.Empty;
    public string? EvidenceSummary { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public DateTimeOffset RetrievedAtUtc { get; set; }
    public string? RawPayloadHash { get; set; }
}

/// <summary>Immutable scoring snapshot shown to a human reviewer.</summary>
public sealed class CrmProspectingScoreDto
{
    public long BasicId { get; set; }
    public string ScorecardVersion { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Priority { get; set; }
    public int IdentityPoints { get; set; }
    public int ProcurementPoints { get; set; }
    public int ProductFitPoints { get; set; }
    public int MarketFitPoints { get; set; }
    public int RecencyPoints { get; set; }
    public int EvidencePoints { get; set; }
    public int ContactPoints { get; set; }
    public string ReviewGatesJson { get; set; } = "[]";
    public DateTimeOffset EvaluatedAtUtc { get; set; }
}

/// <summary>Review detail combines a Candidate with its traceable evidence and immutable score history.</summary>
public sealed class CrmProspectingCandidateReviewDetailDto
{
    public CrmProspectingCandidateDto Candidate { get; set; } = new();
    public IReadOnlyList<CrmProspectingEvidenceDto> Evidence { get; set; } = [];
    public IReadOnlyList<CrmProspectingScoreDto> ScoreHistory { get; set; } = [];
}

/// <summary>
/// Aggregated provider-step progress for one Run. Terminal steps are Succeeded, Failed,
/// Skipped and NeedsReview: they no longer advance without action, so they count toward
/// completion while NeedsReview stays separately visible for human recovery.
/// </summary>
public sealed class CrmProspectingRunProgressDto
{
    public int TotalSteps { get; set; }
    public int SucceededSteps { get; set; }
    public int FailedSteps { get; set; }
    public int RunningSteps { get; set; }
    public int QueuedSteps { get; set; }
    public int NeedsReviewSteps { get; set; }
    public int SkippedSteps { get; set; }
    public int CompletedSteps { get; set; }
    /// <summary>Completed share of total steps in percent; 0 when the plan has no materialized step.</summary>
    public int Percent { get; set; }
}

/// <summary>One durable provider execution checkpoint within an immutable Run plan.</summary>
public sealed class CrmProspectingRunProviderExecutionDto
{
    public long BasicId { get; set; }
    public long RunId { get; set; }
    public int PlanVersion { get; set; }
    public int PlanOrdinal { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int RawRecordCount { get; set; }
    public int ProjectedRecordCount { get; set; }
    public int FailureCount { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureSummary { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Read model for the Run monitoring panel: progress aggregate plus per-provider rows.</summary>
public sealed class CrmProspectingRunMonitorDto
{
    public long RunId { get; set; }
    public CrmProspectingRunProgressDto Progress { get; set; } = new();
    public IReadOnlyList<CrmProspectingRunProviderExecutionDto> Executions { get; set; } = [];
}

/// <summary>Candidate review-queue query parameters: always scoped to one Run.</summary>
public sealed class CrmProspectingCandidatePageQueryDto : BasicAppPRDto
{
    public long RunId { get; set; }
    public int? Status { get; set; }

    /// <summary>Tri-state shortlist filter: null = all, true = shortlisted only, false = not shortlisted.</summary>
    public bool? Shortlisted { get; set; }
}

/// <summary>
/// Input for a deterministic Super Search planning preview. Mirrors the domain input; the preview
/// never calls providers, changes Campaign/Strategy state, raises budgets or creates CRM records.
/// </summary>
public sealed class CrmProspectingSuperSearchPreviewInputDto
{
    public IReadOnlyList<string> Products { get; set; } = [];
    public IReadOnlyList<string> Markets { get; set; } = [];
    public IReadOnlyList<string> CustomerTypes { get; set; } = [];
    public IReadOnlyList<string> Industries { get; set; } = [];
    public IReadOnlyList<string> ProviderCodes { get; set; } = [];
    public IReadOnlyList<string> ExcludedTerms { get; set; } = [];
    public IReadOnlyList<string> ExcludedDomains { get; set; } = [];

    /// <summary>Wire value of <see cref="CrmProspectingSuperSearchDepth"/> (0 = Balanced, 1 = Deep, 2 = Extreme).</summary>
    public int Depth { get; set; }
    public int? RequestedRounds { get; set; }
}

/// <summary>One provider-specific query cell within a planned round.</summary>
public sealed class CrmProspectingSuperSearchQueryCellDto
{
    public string ProviderCode { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string CustomerType { get; set; } = string.Empty;
    public string ThemeCode { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
}

/// <summary>One deterministic search round; exclusions are carried into every query cell.</summary>
public sealed class CrmProspectingSuperSearchRoundDto
{
    public int RoundNumber { get; set; }
    public string ThemeCode { get; set; } = string.Empty;
    public string ThemeLabel { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string CustomerType { get; set; } = string.Empty;
    public IReadOnlyList<string> ExcludedTerms { get; set; } = [];
    public IReadOnlyList<string> ExcludedDomains { get; set; } = [];
    public IReadOnlyList<CrmProspectingSuperSearchQueryCellDto> QueryCells { get; set; } = [];
    public string Fingerprint { get; set; } = string.Empty;
}

/// <summary>Read-only Super Search plan preview: nothing is persisted and no Run is created.</summary>
public sealed class CrmProspectingSuperSearchPreviewDto
{
    public string ScopeFingerprint { get; set; } = string.Empty;
    public int MaximumRounds { get; set; }
    public int PlannedRounds { get; set; }
    public int ProviderCount { get; set; }
    public int TotalQueryCells { get; set; }
    public IReadOnlyList<CrmProspectingSuperSearchRoundDto> Rounds { get; set; } = [];
}

/// <summary>
/// Save request for a durable mission. The plan is recomputed by the server from these inputs;
/// a client-supplied plan snapshot is never trusted. RequestKey is the idempotency boundary.
/// </summary>
public sealed class CrmProspectingSuperSearchMissionCreateDto
{
    public long CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RequestKey { get; set; } = string.Empty;
    public CrmProspectingSuperSearchPreviewInputDto Plan { get; set; } = new();
}

/// <summary>Natural-language objective to be mapped into a bounded Super Search plan input.</summary>
public sealed class CrmProspectingSuperSearchObjectiveParseDto
{
    public string Objective { get; set; } = string.Empty;
}

/// <summary>Explainable evidence for one parsed field (marker and normalized values).</summary>
public sealed class CrmProspectingObjectiveMatchDto
{
    public string Field { get; set; } = string.Empty;
    public string Marker { get; set; } = string.Empty;
    public IReadOnlyList<string> Values { get; set; } = [];
}

/// <summary>
/// Controlled parse result: the input is ready for the planner preview but never executes anything;
/// matches and warnings let the reviewer see exactly how free text was interpreted.
/// </summary>
public sealed class CrmProspectingSuperSearchObjectiveParseResultDto
{
    public CrmProspectingSuperSearchPreviewInputDto Input { get; set; } = new();
    public IReadOnlyList<CrmProspectingObjectiveMatchDto> Matches { get; set; } = [];
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

/// <summary>Create request for a timezone-aware recurring prospecting schedule.</summary>
public sealed class CrmProspectingScheduleCreateDto
{
    public long CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RequestKeyPrefix { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>Wire value of CrmProspectingScheduleFrequency (0 = Daily, 1 = Weekly, 2 = Monthly).</summary>
    public int Frequency { get; set; }
    public int Interval { get; set; } = 1;
    public int TimeOfDayMinutes { get; set; }
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public DateTimeOffset StartDateUtc { get; set; }
    public DateTimeOffset? EndDateUtc { get; set; }
}

/// <summary>Approval decision for a pending schedule.</summary>
public sealed class CrmProspectingScheduleDecisionDto
{
    public long ScheduleId { get; set; }
    public bool Approve { get; set; }
}

/// <summary>Recurring schedule read model; NextRunAtUtc is null once the end date is reached.</summary>
public sealed class CrmProspectingScheduleDto
{
    public long BasicId { get; set; }
    public long CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RequestKeyPrefix { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public int Frequency { get; set; }
    public int Interval { get; set; }
    public int TimeOfDayMinutes { get; set; }
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public DateTimeOffset StartDateUtc { get; set; }
    public DateTimeOffset? EndDateUtc { get; set; }

    /// <summary>Wire value of CrmProspectingScheduleStatus.</summary>
    public int Status { get; set; }
    public DateTimeOffset? NextRunAtUtc { get; set; }
    public DateTimeOffset? LastRunAtUtc { get; set; }
    public long? LastRunId { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}

/// <summary>Durable mission read model: the frozen plan plus its execution link.</summary>
public sealed class CrmProspectingSuperSearchMissionDto
{
    public long BasicId { get; set; }
    public long CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RequestKey { get; set; } = string.Empty;
    public string ScopeFingerprint { get; set; } = string.Empty;
    public int Depth { get; set; }
    public int PlannedRounds { get; set; }
    public int ProviderCount { get; set; }

    /// <summary>Wire value of CrmProspectingSuperSearchMissionStatus (0 = Planned, 1 = Executed).</summary>
    public int Status { get; set; }
    public long? RunId { get; set; }
    public CrmProspectingSuperSearchPreviewDto Plan { get; set; } = new();
    public DateTimeOffset CreatedTime { get; set; }
}
