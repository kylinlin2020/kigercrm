using XiHan.BasicApp.Core.Dtos;
namespace XiHan.BasicApp.Crm.Application.Dtos;
public sealed class CrmActivityPageQueryDto : BasicAppPRDto
{
    public string? RelatedEntityType { get; set; }
    public long? RelatedEntityId { get; set; }
    public int? ActivityType { get; set; }
    public long? OwnerUserId { get; set; }
    public DateTime? OccurredFrom { get; set; }
    public DateTime? OccurredTo { get; set; }
    public string? Keyword { get; set; }
}
public sealed class CrmActivityListItemDto : BasicAppDto
{
    public string RelatedEntityType { get; set; } = string.Empty;
    public long RelatedEntityId { get; set; }
    public int ActivityType { get; set; }
    public string Subject { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public long? OwnerUserId { get; set; }
}
