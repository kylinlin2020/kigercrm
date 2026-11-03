using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// A versioned commercial quotation. Amounts and customer details are snapshots for auditability.
/// </summary>
[SugarTable(TableName = "Crm_Quote", TableDescription = "KIGER CRM customer quotations")]
[SugarIndex("UX_{table}_TeId_No_Ve", nameof(TenantId), OrderByType.Asc, nameof(QuoteNumber), OrderByType.Asc, nameof(Version), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_TeId_Cu_St", nameof(TenantId), OrderByType.Asc, nameof(CustomerId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
[SugarIndex("IX_{table}_TeId_Ow_Cr", nameof(TenantId), OrderByType.Asc, nameof(OwnerUserId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
public sealed class SysCrmQuote : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Quote_Number", Length = 40, IsNullable = false)]
    public string QuoteNumber { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Version", IsNullable = false)]
    public int Version { get; set; } = 1;

    [SugarColumn(ColumnName = "Previous_Version_Id", IsNullable = true)]
    public long? PreviousVersionId { get; set; }

    [SugarColumn(ColumnName = "Customer_Id", IsNullable = false)]
    public long CustomerId { get; set; }

    [SugarColumn(ColumnName = "Contact_Id", IsNullable = true)]
    public long? ContactId { get; set; }

    [SugarColumn(ColumnName = "Opportunity_Id", IsNullable = true)]
    public long? OpportunityId { get; set; }

    [SugarColumn(ColumnName = "Customer_Name_Snapshot", Length = 200, IsNullable = false)]
    public string CustomerNameSnapshot { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Contact_Name_Snapshot", Length = 200, IsNullable = true)]
    public string? ContactNameSnapshot { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false)]
    public CrmQuoteStatus Status { get; set; } = CrmQuoteStatus.Draft;

    [SugarColumn(ColumnName = "Issue_Date", IsNullable = false)]
    public DateTime IssueDate { get; set; }

    [SugarColumn(ColumnName = "Valid_Until", IsNullable = true)]
    public DateTime? ValidUntil { get; set; }

    [SugarColumn(ColumnName = "Currency", Length = 3, IsNullable = false)]
    public string Currency { get; set; } = "USD";

    [SugarColumn(ColumnName = "Subtotal", ColumnDataType = "decimal(18,2)", IsNullable = false)]
    public decimal Subtotal { get; set; }

    [SugarColumn(ColumnName = "Discount_Total", ColumnDataType = "decimal(18,2)", IsNullable = false)]
    public decimal DiscountTotal { get; set; }

    [SugarColumn(ColumnName = "Tax_Total", ColumnDataType = "decimal(18,2)", IsNullable = false)]
    public decimal TaxTotal { get; set; }

    [SugarColumn(ColumnName = "Grand_Total", ColumnDataType = "decimal(18,2)", IsNullable = false)]
    public decimal GrandTotal { get; set; }

    [SugarColumn(ColumnName = "Owner_User_Id", IsNullable = true)]
    public long? OwnerUserId { get; set; }

    [SugarColumn(ColumnName = "Converted_Order_Id", IsNullable = true)]
    public long? ConvertedOrderId { get; set; }

    [SugarColumn(ColumnName = "Terms_And_Conditions", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? TermsAndConditions { get; set; }

    [SugarColumn(ColumnName = "Notes", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? Notes { get; set; }
}
