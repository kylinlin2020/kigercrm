using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// Immutable versioned search strategy. Only approved versions may be activated for execution.
/// Provider plans and exclusions are persisted as JSON text, never as executable instructions.
/// </summary>
[SugarTable(TableName = "Crm_Prospecting_Strategy", TableDescription = "KIGER CRM prospecting strategy versions")]
[SugarIndex("UX_{table}_Te_Ca_Ve_IsDe", nameof(TenantId), OrderByType.Asc, nameof(CampaignId), OrderByType.Asc, nameof(Version), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_Te_Ca_St", nameof(TenantId), OrderByType.Asc, nameof(CampaignId), OrderByType.Asc, nameof(ApprovalStatus), OrderByType.Asc)]
public sealed class SysCrmProspectingStrategy : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Campaign_Id", IsNullable = false)]
    public long CampaignId { get; set; }

    [SugarColumn(ColumnName = "Version", IsNullable = false)]
    public int Version { get; set; }

    [SugarColumn(ColumnName = "Name", Length = 160, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Search_Objective", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string SearchObjective { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Provider_Plan_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string ProviderPlanJson { get; set; } = "[]";

    [SugarColumn(ColumnName = "Excluded_Domains_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? ExcludedDomainsJson { get; set; }

    [SugarColumn(ColumnName = "Approval_Status", IsNullable = false, DefaultValue = "0")]
    public int ApprovalStatus { get; set; }

    [SugarColumn(ColumnName = "Approved_By", IsNullable = true)]
    public long? ApprovedBy { get; set; }

    [SugarColumn(ColumnName = "Approved_At", IsNullable = true)]
    public DateTimeOffset? ApprovedAt { get; set; }

    [SugarColumn(ColumnName = "Activated_At", IsNullable = true)]
    public DateTimeOffset? ActivatedAt { get; set; }
}
