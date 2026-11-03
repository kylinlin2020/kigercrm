using XiHan.BasicApp.Core.Dtos;

namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmOpportunityCreateDto
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
    public string? LostReason { get; set; }
}

public sealed class CrmOpportunityUpdateDto : BasicAppUDto
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
    public string? LostReason { get; set; }
}
