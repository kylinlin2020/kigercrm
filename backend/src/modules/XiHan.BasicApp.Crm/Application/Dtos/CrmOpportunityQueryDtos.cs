using XiHan.BasicApp.Core.Dtos;

namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmOpportunityPageQueryDto : BasicAppPRDto
{
    public string? Keyword { get; set; }
    public long? CustomerId { get; set; }
    public int? Stage { get; set; }
    public long? OwnerUserId { get; set; }
}

public sealed class CrmOpportunityListItemDto : BasicAppDto
{
    public long CustomerId { get; set; }
    public long? PrimaryContactId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Stage { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime? ExpectedCloseDate { get; set; }
    public int? Probability { get; set; }
    public long? OwnerUserId { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}

public sealed class CrmOpportunityDetailDto : CrmOpportunityListItemDto
{
    public string? LostReason { get; set; }
}
