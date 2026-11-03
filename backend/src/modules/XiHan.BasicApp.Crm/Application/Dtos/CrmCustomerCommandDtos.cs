using XiHan.BasicApp.Core.Dtos;

namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmCustomerCreateDto
{
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? Website { get; set; }
    public string? CountryRegion { get; set; }
    public string? Industry { get; set; }
    public string? Source { get; set; }
    public long? OwnerUserId { get; set; }
    public string? Notes { get; set; }
}

public sealed class CrmCustomerUpdateDto : BasicAppUDto
{
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? Website { get; set; }
    public string? CountryRegion { get; set; }
    public string? Industry { get; set; }
    public string? Source { get; set; }
    public long? OwnerUserId { get; set; }
    public string? Notes { get; set; }
}
