using XiHan.BasicApp.Crm.Domain.Entities;
namespace XiHan.BasicApp.Crm.Domain.DomainServices;
public interface ICrmActivityDomainService
{
    Task<SysCrmActivity> CreateAsync(CrmActivityCommand command, CancellationToken cancellationToken = default);
    Task<SysCrmActivity> UpdateAsync(long id, CrmActivityCommand command, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
public sealed record CrmActivityCommand(string RelatedEntityType, long RelatedEntityId, int ActivityType, string Subject, string? Content, DateTime OccurredAt, long? OwnerUserId, string? ExternalSystem, string? ExternalId);