using XiHan.BasicApp.Core.Dtos;

namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmCustomerDto : BasicAppDto
{
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? Website { get; set; }
    public string? CountryRegion { get; set; }
    public string? Industry { get; set; }
    public string? Source { get; set; }
    public long? OwnerUserId { get; set; }
    public string? Notes { get; set; }
}

public sealed class CrmContactDto : BasicAppDto
{
    public long CustomerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? JobTitle { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public long? OwnerUserId { get; set; }
    public bool IsPrimary { get; set; }
    public string? Notes { get; set; }
}

public sealed class CrmOpportunityDto : BasicAppDto
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

public sealed class CrmActivityDto : BasicAppDto
{
    public string RelatedEntityType { get; set; } = string.Empty;
    public long RelatedEntityId { get; set; }
    public int ActivityType { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Content { get; set; }
    public DateTime OccurredAt { get; set; }
    public long? OwnerUserId { get; set; }
    public string? ExternalSystem { get; set; }
    public string? ExternalId { get; set; }
}

public sealed class CrmTaskDto : BasicAppDto
{
    public string? RelatedEntityType { get; set; }
    public long? RelatedEntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? DueAt { get; set; }
    public int Status { get; set; }
    public int Priority { get; set; }
    public long? AssigneeUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
}
