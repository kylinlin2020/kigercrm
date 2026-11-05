using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Immutable product/service snapshot line belonging to one quotation version.</summary>
[SugarTable(TableName = "Crm_Quote_Line", TableDescription = "KIGER CRM quotation line snapshots")]
[SugarIndex("IX_{table}_TeId_Qt_So", nameof(TenantId), OrderByType.Asc, nameof(QuoteId), OrderByType.Asc, nameof(SortOrder), OrderByType.Asc)]
public sealed class SysCrmQuoteLine : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Quote_Id", IsNullable = false)]
    public long QuoteId { get; set; }

    [SugarColumn(ColumnName = "Product_Code", Length = 100, IsNullable = true)]
    public string? ProductCode { get; set; }

    [SugarColumn(ColumnName = "Description", Length = 500, IsNullable = false)]
    public string Description { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Quantity", ColumnDataType = "decimal(18,4)", IsNullable = false)]
    public decimal Quantity { get; set; }

    [SugarColumn(ColumnName = "Unit", Length = 30, IsNullable = true)]
    public string? Unit { get; set; }

    [SugarColumn(ColumnName = "Unit_Price", ColumnDataType = "decimal(18,4)", IsNullable = false)]
    public decimal UnitPrice { get; set; }

    [SugarColumn(ColumnName = "Discount_Rate", ColumnDataType = "decimal(9,4)", IsNullable = false)]
    public decimal DiscountRate { get; set; }

    [SugarColumn(ColumnName = "Tax_Rate", ColumnDataType = "decimal(9,4)", IsNullable = false)]
    public decimal TaxRate { get; set; }

    [SugarColumn(ColumnName = "Line_Subtotal", ColumnDataType = "decimal(18,2)", IsNullable = false)]
    public decimal LineSubtotal { get; set; }

    [SugarColumn(ColumnName = "Line_Discount", ColumnDataType = "decimal(18,2)", IsNullable = false)]
    public decimal LineDiscount { get; set; }

    [SugarColumn(ColumnName = "Line_Tax", ColumnDataType = "decimal(18,2)", IsNullable = false)]
    public decimal LineTax { get; set; }

    [SugarColumn(ColumnName = "Line_Total", ColumnDataType = "decimal(18,2)", IsNullable = false)]
    public decimal LineTotal { get; set; }

    [SugarColumn(ColumnName = "Sort_Order", IsNullable = false)]
    public int SortOrder { get; set; }
}
