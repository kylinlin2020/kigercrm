using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Potential revenue associated with a customer account.</summary>
[SugarTable(TableName = "Crm_Opportunity", TableDescription = "KIGER CRM sales opportunities")]
[SugarIndex("IX_{table}_TeId_St_CrTi", nameof(TenantId), OrderByType.Asc, nameof(Stage), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[SugarIndex("IX_{table}_TeId_CuId", nameof(TenantId), OrderByType.Asc, nameof(CustomerId), OrderByType.Asc)]
[SugarIndex("IX_{table}_TeId_OwId", nameof(TenantId), OrderByType.Asc, nameof(OwnerUserId), OrderByType.Asc)]
public sealed class SysCrmOpportunity : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Customer_Id", IsNullable = false)]
    public long CustomerId { get; set; }

    [SugarColumn(ColumnName = "Primary_Contact_Id", IsNullable = true)]
    public long? PrimaryContactId { get; set; }

    [SugarColumn(ColumnName = "Name", Length = 200, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Stage", IsNullable = false)]
    public CrmOpportunityStage Stage { get; set; } = CrmOpportunityStage.Qualification;

    [SugarColumn(ColumnName = "Amount", ColumnDataType = "decimal(18,2)", IsNullable = false)]
    public decimal Amount { get; set; }

    [SugarColumn(ColumnName = "Currency", Length = 3, IsNullable = false)]
    public string Currency { get; set; } = "USD";

    [SugarColumn(ColumnName = "Expected_Close_Date", IsNullable = true)]
    public DateTime? ExpectedCloseDate { get; set; }

    [SugarColumn(ColumnName = "Probability", IsNullable = true)]
    public int? Probability { get; set; }

    [SugarColumn(ColumnName = "Owner_User_Id", IsNullable = true)]
    public long? OwnerUserId { get; set; }

    [SugarColumn(ColumnName = "Lost_Reason", Length = 500, IsNullable = true)]
    public string? LostReason { get; set; }
}
