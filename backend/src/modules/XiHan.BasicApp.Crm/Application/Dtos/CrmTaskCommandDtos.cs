using XiHan.BasicApp.Core.Dtos;
namespace XiHan.BasicApp.Crm.Application.Dtos;
public sealed class CrmTaskCreateDto
{
    public string? RelatedEntityType { get; set; }
    public long? RelatedEntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? DueAt { get; set; }
    public int Priority { get; set; } = 2;
    public long? AssigneeUserId { get; set; }
}
public sealed class CrmTaskUpdateDto : BasicAppUDto
{
    public string? RelatedEntityType { get; set; }
    public long? RelatedEntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? DueAt { get; set; }
    public int Status { get; set; }
    public int Priority { get; set; } = 2;
    public long? AssigneeUserId { get; set; }
}