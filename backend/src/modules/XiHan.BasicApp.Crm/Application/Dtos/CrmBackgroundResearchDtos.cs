namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmBackgroundResearchReportDto
{
    public long BasicId { get; set; }
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public long? GeneratedByUserId { get; set; }
    public int EvidenceCount { get; set; }
    public int OpportunityCount { get; set; }
}

public sealed class CrmResearchEvidenceDto
{
    public long BasicId { get; set; }
    public long ReportId { get; set; }
    public int Source { get; set; }
    public string FactKey { get; set; } = string.Empty;
    public string FactValue { get; set; } = string.Empty;
    public string? SourceRef { get; set; }
    public int Confidence { get; set; }
}

public sealed class CrmMarketOpportunityDto
{
    public long BasicId { get; set; }
    public long ReportId { get; set; }
    public long CustomerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FactKey { get; set; } = string.Empty;
    public string FactValue { get; set; } = string.Empty;
    public int PriorityScore { get; set; }
    public int Status { get; set; }
}

public sealed class CrmBackgroundResearchDetailDto
{
    public CrmBackgroundResearchReportDto Report { get; set; } = new();
    public IReadOnlyList<CrmResearchEvidenceDto> Evidence { get; set; } = [];
    public IReadOnlyList<CrmMarketOpportunityDto> Opportunities { get; set; } = [];
}

public sealed class CrmMarketOpportunityReviewDto
{
    public long OpportunityId { get; set; }
    public int Status { get; set; }
}
