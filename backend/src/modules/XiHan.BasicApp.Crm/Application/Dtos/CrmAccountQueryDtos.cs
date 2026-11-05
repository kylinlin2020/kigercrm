using XiHan.BasicApp.Core.Dtos;

namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmCustomerPageQueryDto : BasicAppPRDto
{
    public string? Keyword { get; set; }
    public string? CountryRegion { get; set; }
    public string? Industry { get; set; }
    public long? OwnerUserId { get; set; }
}

public class CrmCustomerListItemDto : BasicAppDto
{
    public string Name { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? CountryRegion { get; set; }
    public string? Industry { get; set; }
    public long? OwnerUserId { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}

public sealed class CrmCustomerDetailDto : CrmCustomerListItemDto
{
    public string? LegalName { get; set; }
    public string? Source { get; set; }
    public string? Notes { get; set; }
}

public sealed class CrmContactPageQueryDto : BasicAppPRDto
{
    public long? CustomerId { get; set; }
    public string? Keyword { get; set; }
    public long? OwnerUserId { get; set; }
}

public sealed class CrmContactListItemDto : BasicAppDto
{
    public long CustomerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? JobTitle { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsPrimary { get; set; }
    public long? OwnerUserId { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}
