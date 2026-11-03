using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.DomainServices;

public interface ICrmOpportunityDomainService
{
    Task<SysCrmOpportunity> CreateAsync(CrmOpportunityCreateCommand command, CancellationToken cancellationToken = default);
    Task<SysCrmOpportunity> UpdateAsync(CrmOpportunityUpdateCommand command, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public sealed record CrmOpportunityCreateCommand(long CustomerId, long? PrimaryContactId, string Name, int Stage, decimal Amount, string Currency, DateTime? ExpectedCloseDate, int? Probability, long? OwnerUserId, string? LostReason);
public sealed record CrmOpportunityUpdateCommand(long Id, long CustomerId, long? PrimaryContactId, string Name, int Stage, decimal Amount, string Currency, DateTime? ExpectedCloseDate, int? Probability, long? OwnerUserId, string? LostReason);
