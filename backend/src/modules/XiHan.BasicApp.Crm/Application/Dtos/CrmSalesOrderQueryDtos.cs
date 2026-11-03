using XiHan.BasicApp.Core.Dtos;

namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmSalesOrderPageQueryDto : BasicAppPRDto
{
    public string? Keyword { get; set; }
    public long? CustomerId { get; set; }
    public int? Status { get; set; }
    public long? OwnerUserId { get; set; }
}

public sealed class CrmSalesOrderListItemDto : BasicAppDto
{
    public string OrderNumber { get; set; } = string.Empty;
    public long? SourceQuoteId { get; set; }
    public long CustomerId { get; set; }
    public string CustomerNameSnapshot { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTime OrderDate { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal GrandTotal { get; set; }
    public long? OwnerUserId { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}
