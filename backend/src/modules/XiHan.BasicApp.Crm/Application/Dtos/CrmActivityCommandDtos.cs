using XiHan.BasicApp.Core.Dtos;
namespace XiHan.BasicApp.Crm.Application.Dtos;
public sealed class CrmActivityCreateDto
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
public sealed class CrmActivityUpdateDto : BasicAppUDto
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