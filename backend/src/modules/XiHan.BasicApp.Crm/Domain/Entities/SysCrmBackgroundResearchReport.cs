using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// A point-in-time, tenant-scoped research dossier for one customer. Regenerating supersedes the
/// previous report instead of mutating it, so a customer's research history stays auditable.
/// </summary>
[SugarTable(TableName = "Crm_Background_Research_Report", TableDescription = "CRM customer background research reports")]
[SugarIndex("IX_{table}_Te_Cu_St", nameof(TenantId), OrderByType.Asc, nameof(CustomerId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
public sealed class SysCrmBackgroundResearchReport : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Customer_Id", IsNullable = false)]
    public long CustomerId { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmResearchReportStatus Status { get; set; }

    [SugarColumn(ColumnName = "Generated_At_Utc", IsNullable = false)]
    public DateTimeOffset GeneratedAtUtc { get; set; }

    [SugarColumn(ColumnName = "Generated_By_User_Id", IsNullable = true)]
    public long? GeneratedByUserId { get; set; }

    [SugarColumn(ColumnName = "Evidence_Count", IsNullable = false, DefaultValue = "0")]
    public int EvidenceCount { get; set; }

    [SugarColumn(ColumnName = "Opportunity_Count", IsNullable = false, DefaultValue = "0")]
    public int OpportunityCount { get; set; }
}
