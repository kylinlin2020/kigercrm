using XiHan.BasicApp.Core.Dtos;

namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmQuotePageQueryDto : BasicAppPRDto
{
    public string? Keyword { get; set; }
    public long? CustomerId { get; set; }
    public int? Status { get; set; }
    public long? OwnerUserId { get; set; }
}

public sealed class CrmQuoteListItemDto : BasicAppDto
{
    public string QuoteNumber { get; set; } = string.Empty;
    public int Version { get; set; }
    public long CustomerId { get; set; }
    public string CustomerNameSnapshot { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal GrandTotal { get; set; }
    public long? OwnerUserId { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}

public sealed class CrmQuoteDetailDto : CrmQuoteListItemDto
{
    public long? ContactId { get; set; }
    public long? OpportunityId { get; set; }
    public string? ContactNameSnapshot { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? Notes { get; set; }
    public long? ConvertedOrderId { get; set; }
}

public sealed class CrmQuoteLinePageQueryDto : BasicAppPRDto
{
    public long QuoteId { get; set; }
}

public sealed class CrmQuoteLineDto : BasicAppDto
{
    public string? ProductCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal TaxRate { get; set; }
    public decimal LineSubtotal { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal LineTax { get; set; }
    public decimal LineTotal { get; set; }
    public int SortOrder { get; set; }
}
