using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Commercial product/service snapshot copied from the source quotation.</summary>
[SugarTable(TableName = "Crm_Sales_Order_Line", TableDescription = "KIGER CRM sales order line snapshots")]
[SugarIndex("IX_{table}_TeId_Or_So", nameof(TenantId), OrderByType.Asc, nameof(SalesOrderId), OrderByType.Asc, nameof(SortOrder), OrderByType.Asc)]
[SugarIndex("UX_{table}_TeId_Or_SqLi_IsDe", nameof(TenantId), OrderByType.Asc, nameof(SalesOrderId), OrderByType.Asc, nameof(SourceQuoteLineId), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
public sealed class SysCrmSalesOrderLine : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Sales_Order_Id", IsNullable = false)]
    public long SalesOrderId { get; set; }

    [SugarColumn(ColumnName = "Source_Quote_Line_Id", IsNullable = true)]
    public long? SourceQuoteLineId { get; set; }

    [SugarColumn(ColumnName = "Product_Code", Length = 100, IsNullable = true)]
    public string? ProductCode { get; set; }

    [SugarColumn(ColumnName = "Description", Length = 500, IsNullable = false)]
    public string Description { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Quantity", ColumnDataType = "decimal(18,4)", IsNullable = false)]
    public decimal Quantity { get; set; }

    /// <summary>Quantity reserved by draft shipments; released on cancellation or consumed on dispatch.</summary>
    [SugarColumn(ColumnName = "Reserved_Quantity", ColumnDataType = "decimal(18,4)", IsNullable = false, DefaultValue = "0")]
    public decimal ReservedQuantity { get; set; }

    /// <summary>Quantity physically dispatched against this order line.</summary>
    [SugarColumn(ColumnName = "Shipped_Quantity", ColumnDataType = "decimal(18,4)", IsNullable = false, DefaultValue = "0")]
    public decimal ShippedQuantity { get; set; }

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
