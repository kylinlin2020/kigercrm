using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.DomainServices;

public interface ICrmCustomerDomainService
{
    Task<SysCrmCustomer> CreateAsync(CrmCustomerCreateCommand command, CancellationToken cancellationToken = default);
    Task<SysCrmCustomer> UpdateAsync(CrmCustomerUpdateCommand command, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public sealed record CrmCustomerCreateCommand(string Name, string? LegalName, string? Website, string? CountryRegion, string? Industry, string? Source, long? OwnerUserId, string? Notes);
public sealed record CrmCustomerUpdateCommand(long Id, string Name, string? LegalName, string? Website, string? CountryRegion, string? Industry, string? Source, long? OwnerUserId, string? Notes);
