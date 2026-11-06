namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmSalesOrderLineUpdateDto
{
    public long Id { get; set; }
    public long? SourceQuoteLineId { get; set; }
    public string? ProductCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal TaxRate { get; set; }
    public int SortOrder { get; set; }
}

public sealed class CrmSalesOrderUpdateDto
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public long? ContactId { get; set; }
    public long? OpportunityId { get; set; }
    public DateTime OrderDate { get; set; }
    public string Currency { get; set; } = "USD";
    public string? CustomerPurchaseOrderNumber { get; set; }
    public string? PaymentTerms { get; set; }
    public string? DeliveryTerms { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? Notes { get; set; }
    public List<CrmSalesOrderLineUpdateDto> Lines { get; set; } = [];
}
