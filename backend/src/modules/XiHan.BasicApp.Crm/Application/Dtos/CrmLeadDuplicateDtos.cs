using XiHan.BasicApp.Core.Dtos;

namespace XiHan.BasicApp.Crm.Application.Dtos;

/// <summary>Input signals used to look for possible duplicate leads.</summary>
public sealed class CrmLeadDuplicateCheckDto
{
    public long? ExcludeLeadId { get; set; }
    public string? CompanyName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
}

/// <summary>A possible duplicate lead and the matching signals.</summary>
public sealed class CrmLeadDuplicateCandidateDto
{
    public CrmLeadListItemDto Candidate { get; set; } = new();
    public int Score { get; set; }
    public string Confidence { get; set; } = string.Empty;
    public IReadOnlyList<string> MatchedSignals { get; set; } = [];
}
