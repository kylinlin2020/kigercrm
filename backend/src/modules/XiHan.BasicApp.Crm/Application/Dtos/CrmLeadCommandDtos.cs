using XiHan.BasicApp.Core.Dtos;

namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmLeadCreateDto
{
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? CountryRegion { get; set; }
    public string? Source { get; set; }
    public string? SourceReference { get; set; }
    public long? OwnerUserId { get; set; }
    public string? Notes { get; set; }
}

public sealed class CrmLeadUpdateDto : BasicAppUDto
{
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? CountryRegion { get; set; }
    public string? Source { get; set; }
    public string? SourceReference { get; set; }
    public int Status { get; set; }
    public long? OwnerUserId { get; set; }
    public string? Notes { get; set; }
}
