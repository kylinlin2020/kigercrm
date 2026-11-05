using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.DomainServices;

public interface ICrmContactDomainService
{
    Task<SysCrmContact> CreateAsync(CrmContactCreateCommand command, CancellationToken cancellationToken = default);
    Task<SysCrmContact> UpdateAsync(CrmContactUpdateCommand command, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public sealed record CrmContactCreateCommand(long CustomerId, string FirstName, string LastName, string? JobTitle, string? Email, string? Phone, string? WhatsApp, long? OwnerUserId, bool IsPrimary, string? Notes);
public sealed record CrmContactUpdateCommand(long Id, long CustomerId, string FirstName, string LastName, string? JobTitle, string? Email, string? Phone, string? WhatsApp, long? OwnerUserId, bool IsPrimary, string? Notes);
