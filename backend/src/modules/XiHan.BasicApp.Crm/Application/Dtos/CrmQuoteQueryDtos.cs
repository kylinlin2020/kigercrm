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
