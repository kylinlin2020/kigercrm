namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmOutreachDraftDto
{
    public long BasicId { get; set; }
    public long EnrollmentId { get; set; }
    public long CandidateId { get; set; }
    public int Step { get; set; }
    public int Channel { get; set; }
    public string RenderedText { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public int Status { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
}

public sealed class CrmOutreachDraftPreviewDto
{
    public long CandidateId { get; set; }
    public string Template { get; set; } = string.Empty;
}

public sealed class CrmOutreachDraftCreateDto
{
    public long EnrollmentId { get; set; }
    public int Step { get; set; }
}

public sealed class CrmOutreachDraftReviewDto
{
    public long DraftId { get; set; }
    public string? Reason { get; set; }
}
