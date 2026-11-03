using XiHan.BasicApp.Crm.Domain.Entities;
namespace XiHan.BasicApp.Crm.Domain.DomainServices;
public interface ICrmTaskDomainService
{
    Task<SysCrmTask> CreateAsync(CrmTaskCreateCommand command, CancellationToken cancellationToken = default);
    Task<SysCrmTask> UpdateAsync(long id, CrmTaskUpdateCommand command, CancellationToken cancellationToken = default);
    Task<SysCrmTask> ChangeStatusAsync(long id, int status, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
public sealed record CrmTaskCreateCommand(string? RelatedEntityType, long? RelatedEntityId, string Title, string? Description, DateTime? DueAt, int Priority, long? AssigneeUserId);
public sealed record CrmTaskUpdateCommand(string? RelatedEntityType, long? RelatedEntityId, string Title, string? Description, DateTime? DueAt, int Status, int Priority, long? AssigneeUserId);