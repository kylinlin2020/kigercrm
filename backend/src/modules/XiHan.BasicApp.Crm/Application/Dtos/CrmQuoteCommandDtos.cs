namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmQuoteLineCreateDto
{
    public string? ProductCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal TaxRate { get; set; }
    public int SortOrder { get; set; }
}

public sealed class CrmQuoteCreateDto
{
    public string QuoteNumber { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public long? ContactId { get; set; }
    public long? OpportunityId { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string Currency { get; set; } = "USD";
    public long? OwnerUserId { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? Notes { get; set; }
    public List<CrmQuoteLineCreateDto> Lines { get; set; } = [];
}

public sealed class CrmQuoteStatusUpdateDto
{
    public long Id { get; set; }
    public int Status { get; set; }
}

public sealed class CrmQuoteDto
{
    public long BasicId { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public int Version { get; set; }
    public long CustomerId { get; set; }
    public long? ContactId { get; set; }
    public long? OpportunityId { get; set; }
    public string CustomerNameSnapshot { get; set; } = string.Empty;
    public string? ContactNameSnapshot { get; set; }
    public int Status { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public long? OwnerUserId { get; set; }
}
