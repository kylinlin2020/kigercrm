using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// A market opportunity proposed from a customer's real CRM signals. PriorityScore is a
/// deterministic function of opportunity amount and win probability; no external model is called.
/// </summary>
[SugarTable(TableName = "Crm_Market_Opportunity", TableDescription = "CRM proposed market opportunities")]
[SugarIndex("IX_{table}_Te_Re_St", nameof(TenantId), OrderByType.Asc, nameof(ReportId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
public sealed class SysCrmMarketOpportunity : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Report_Id", IsNullable = false)]
    public long ReportId { get; set; }

    [SugarColumn(ColumnName = "Customer_Id", IsNullable = false)]
    public long CustomerId { get; set; }

    [SugarColumn(ColumnName = "Title", Length = 200, IsNullable = false)]
    public string Title { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Fact_Key", Length = 80, IsNullable = false)]
    public string FactKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Fact_Value", Length = 500, IsNullable = false)]
    public string FactValue { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Priority_Score", IsNullable = false, DefaultValue = "0")]
    public int PriorityScore { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmMarketOpportunityStatus Status { get; set; }
}
