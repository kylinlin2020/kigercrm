using XiHan.BasicApp.Core.Dtos;

namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmContactCreateDto
{
    public long CustomerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? JobTitle { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public long? OwnerUserId { get; set; }
    public bool IsPrimary { get; set; }
    public string? Notes { get; set; }
}

public sealed class CrmContactUpdateDto : BasicAppUDto
{
    public long CustomerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? JobTitle { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public long? OwnerUserId { get; set; }
    public bool IsPrimary { get; set; }
    public string? Notes { get; set; }
}
