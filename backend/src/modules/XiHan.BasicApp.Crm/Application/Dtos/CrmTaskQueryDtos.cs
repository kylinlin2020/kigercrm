using XiHan.BasicApp.Core.Dtos;
namespace XiHan.BasicApp.Crm.Application.Dtos;
public sealed class CrmTaskPageQueryDto : BasicAppPRDto
{
    public string? RelatedEntityType { get; set; }
    public long? RelatedEntityId { get; set; }
    public int? Status { get; set; }
    public int? Priority { get; set; }
    public long? AssigneeUserId { get; set; }
    public DateTime? DueFrom { get; set; }
    public DateTime? DueTo { get; set; }
    public bool? OverdueOnly { get; set; }
    public string? Keyword { get; set; }
}
public sealed class CrmTaskListItemDto : BasicAppDto
{
    public string? RelatedEntityType { get; set; }
    public long? RelatedEntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime? DueAt { get; set; }
    public int Status { get; set; }
    public int Priority { get; set; }
    public long? AssigneeUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
}
