using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Auditable execution record; task acceptance and task completion are distinct states.</summary>
[SugarTable(TableName = "Crm_Prospecting_Run", TableDescription = "KIGER CRM prospecting execution runs")]
[SugarIndex("IX_{table}_TeId_CrTi", nameof(TenantId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[SugarIndex("IX_{table}_CrId", nameof(CreatedId), OrderByType.Asc)]
[SugarIndex("UX_{table}_Te_Rq", nameof(TenantId), OrderByType.Asc, nameof(RequestKey), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_Te_Ca_St_Cr", nameof(TenantId), OrderByType.Asc, nameof(CampaignId), OrderByType.Asc, nameof(Status), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
public sealed class SysCrmProspectingRun : BasicAppCreationEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Campaign_Id", IsNullable = false)]
    public long CampaignId { get; set; }

    [SugarColumn(ColumnName = "Strategy_Id", IsNullable = false)]
    public long StrategyId { get; set; }

    [SugarColumn(ColumnName = "Strategy_Version", IsNullable = false)]
    public int StrategyVersion { get; set; }

    [SugarColumn(ColumnName = "Request_Key", Length = 100, IsNullable = false)]
    public string RequestKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Payload_Hash", Length = 64, IsNullable = false)]
    public string PayloadHash { get; set; } = string.Empty;

    // Immutable execution inputs captured at queue time; workers must not read mutable Campaign/Strategy fields.
    [SugarColumn(ColumnName = "Snapshot_Search_Objective", Length = 2000, IsNullable = false)]
    public string SnapshotSearchObjective { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Snapshot_Product_Scope", Length = 500, IsNullable = true)]
    public string? SnapshotProductScope { get; set; }

    [SugarColumn(ColumnName = "Snapshot_Target_Markets", Length = 2000, IsNullable = true)]
    public string? SnapshotTargetMarkets { get; set; }

    [SugarColumn(ColumnName = "Snapshot_Ideal_Customer_Profile", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? SnapshotIdealCustomerProfile { get; set; }

    [SugarColumn(ColumnName = "Snapshot_Provider_Plan_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string SnapshotProviderPlanJson { get; set; } = "[]";

    [SugarColumn(ColumnName = "Snapshot_Excluded_Domains_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? SnapshotExcludedDomainsJson { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmProspectingRunStatus Status { get; set; }

    [SugarColumn(ColumnName = "Started_At", IsNullable = true)]
    public DateTimeOffset? StartedAt { get; set; }

    [SugarColumn(ColumnName = "Completed_At", IsNullable = true)]
    public DateTimeOffset? CompletedAt { get; set; }

    [SugarColumn(ColumnName = "Raw_Record_Count", IsNullable = false, DefaultValue = "0")]
    public int RawRecordCount { get; set; }

    [SugarColumn(ColumnName = "Candidate_Count", IsNullable = false, DefaultValue = "0")]
    public int CandidateCount { get; set; }

    [SugarColumn(ColumnName = "Failure_Count", IsNullable = false, DefaultValue = "0")]
    public int FailureCount { get; set; }

    [SugarColumn(ColumnName = "Failure_Summary", Length = 2000, IsNullable = true)]
    public string? FailureSummary { get; set; }

    [SugarColumn(ColumnName = "Raw_Results_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? RawResultsJson { get; set; }
}
