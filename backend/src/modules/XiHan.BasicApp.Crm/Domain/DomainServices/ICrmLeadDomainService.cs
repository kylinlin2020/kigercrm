using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.DomainServices;

public interface ICrmLeadDomainService
{
    Task<SysCrmLead> CreateAsync(CrmLeadCreateCommand command, CancellationToken cancellationToken = default);
    Task<SysCrmLead> UpdateAsync(CrmLeadUpdateCommand command, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public sealed record CrmLeadCreateCommand(string CompanyName, string? ContactName, string? Email, string? Phone, string? Website, string? CountryRegion, string? Source, string? SourceReference, long? OwnerUserId, string? Notes);
public sealed record CrmLeadUpdateCommand(long Id, string CompanyName, string? ContactName, string? Email, string? Phone, string? Website, string? CountryRegion, string? Source, string? SourceReference, int Status, long? OwnerUserId, string? Notes);
