namespace XiHan.BasicApp.AI.Application.Dtos;

public sealed class KnowledgeReviewDecisionDto
{
    public long ReviewId { get; set; }
    public bool Approve { get; set; }
    public string? Comment { get; set; }
}

public sealed class KnowledgeReviewDto
{
    public long BasicId { get; set; }
    public long DocumentId { get; set; }
    public int Status { get; set; }
    public string VersionHash { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public long? ReviewerId { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}
