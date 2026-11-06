using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// Customer sales order. Commercial terms and customer details are copied from the accepted quotation.
/// </summary>
[SugarTable(TableName = "Crm_Sales_Order", TableDescription = "KIGER CRM customer sales orders")]
[SugarIndex("UX_{table}_TeId_No", nameof(TenantId), OrderByType.Asc, nameof(OrderNumber), OrderByType.Asc, true)]
[SugarIndex("UX_{table}_TeId_Sq_IsDe", nameof(TenantId), OrderByType.Asc, nameof(SourceQuoteId), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_TeId_Cu_St", nameof(TenantId), OrderByType.Asc, nameof(CustomerId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
[SugarIndex("IX_{table}_TeId_Ow_Cr", nameof(TenantId), OrderByType.Asc, nameof(OwnerUserId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
public sealed class SysCrmSalesOrder : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Order_Number", Length = 40, IsNullable = false)]
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>SHA-256 fingerprint for a server-numbered direct-create request; null for legacy/manual orders.</summary>
    [SugarColumn(ColumnName = "Creation_Request_Fingerprint", Length = 64, IsNullable = true)]
    public string? CreationRequestFingerprint { get; set; }

    [SugarColumn(ColumnName = "Source_Quote_Id", IsNullable = true)]
    public long? SourceQuoteId { get; set; }

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

    [SugarColumn(ColumnName = "Opportunity_Name_Snapshot", Length = 200, IsNullable = true)]
    public string? OpportunityNameSnapshot { get; set; }

    /// <summary>Incremented by each fulfillment or lifecycle mutation to serialize order-level operations.</summary>
    [SugarColumn(ColumnName = "Fulfillment_Revision", IsNullable = false, DefaultValue = "0")]
    public int FulfillmentRevision { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false)]
    public CrmSalesOrderStatus Status { get; set; } = CrmSalesOrderStatus.Draft;

    [SugarColumn(ColumnName = "Order_Date", IsNullable = false)]
    public DateTime OrderDate { get; set; }

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

    [SugarColumn(ColumnName = "Customer_Purchase_Order_Number", Length = 100, IsNullable = true)]
    public string? CustomerPurchaseOrderNumber { get; set; }

    [SugarColumn(ColumnName = "Payment_Terms", Length = 500, IsNullable = true)]
    public string? PaymentTerms { get; set; }

    [SugarColumn(ColumnName = "Delivery_Terms", Length = 500, IsNullable = true)]
    public string? DeliveryTerms { get; set; }

    [SugarColumn(ColumnName = "Terms_And_Conditions", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? TermsAndConditions { get; set; }

    [SugarColumn(ColumnName = "Notes", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? Notes { get; set; }
}
