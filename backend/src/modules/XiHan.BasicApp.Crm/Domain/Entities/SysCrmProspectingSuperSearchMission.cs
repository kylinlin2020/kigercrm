using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// Durable, immutable Super Search mission: the deterministic planner output frozen at save time
/// plus the request key and the eventual Run link. The mission never executes provider work itself;
/// <c>CreateRunFromSuperSearchMission</c> delegates to the existing approved Run pipeline.
/// </summary>
[SugarTable(TableName = "Crm_Prospecting_SuperSearch_Mission", TableDescription = "KIGER CRM prospecting Super Search missions")]
[SugarIndex("IX_{table}_Te_Ca_CrTi", nameof(TenantId), OrderByType.Asc, nameof(CampaignId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[SugarIndex("UX_{table}_Te_Rq_IsDe", nameof(TenantId), OrderByType.Asc, nameof(RequestKey), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
public sealed class SysCrmProspectingSuperSearchMission : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Campaign_Id", IsNullable = false)]
    public long CampaignId { get; set; }

    [SugarColumn(ColumnName = "Name", Length = 160, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Idempotency key shared with the Run: one request key yields at most one mission and one Run.</summary>
    [SugarColumn(ColumnName = "Request_Key", Length = 100, IsNullable = false)]
    public string RequestKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Scope_Fingerprint", Length = 64, IsNullable = false)]
    public string ScopeFingerprint { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Depth", IsNullable = false, DefaultValue = "0")]
    public int Depth { get; set; }

    [SugarColumn(ColumnName = "Planned_Rounds", IsNullable = false, DefaultValue = "0")]
    public int PlannedRounds { get; set; }

    [SugarColumn(ColumnName = "Provider_Count", IsNullable = false, DefaultValue = "0")]
    public int ProviderCount { get; set; }

    /// <summary>Distinct provider codes selected by the plan; must match the approved strategy at execution time.</summary>
    [SugarColumn(ColumnName = "Provider_Plan_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string ProviderPlanJson { get; set; } = "[]";

    /// <summary>Frozen preview DTO produced by the planner; stored for audit and replay, never trusted from the client.</summary>
    [SugarColumn(ColumnName = "Plan_Snapshot_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string PlanSnapshotJson { get; set; } = "{}";

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmProspectingSuperSearchMissionStatus Status { get; set; }

    [SugarColumn(ColumnName = "Run_Id", IsNullable = true)]
    public long? RunId { get; set; }
}
