using XiHan.BasicApp.Core.Dtos;

namespace XiHan.BasicApp.Crm.Application.Dtos;

/// <summary>Lead list query parameters.</summary>
public sealed class CrmLeadPageQueryDto : BasicAppPRDto
{
    public string? Keyword { get; set; }
    public int? Status { get; set; }
    public string? CountryRegion { get; set; }
}

/// <summary>Lead list projection.</summary>
public class CrmLeadListItemDto : BasicAppDto
{
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? CountryRegion { get; set; }
    public string? Source { get; set; }
    public int Status { get; set; }
    public int Score { get; set; }
    public long? OwnerUserId { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}

/// <summary>Lead detail projection.</summary>
public sealed class CrmLeadDetailDto : CrmLeadListItemDto
{
    public string? Website { get; set; }
    public string? SourceReference { get; set; }
    public long? ConvertedCustomerId { get; set; }
    public long? ConvertedContactId { get; set; }
    public DateTimeOffset? LastContactedAt { get; set; }
    public string? Notes { get; set; }
}
