using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
namespace XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;
public sealed class CrmTaskDomainService(ICrmTaskRepository repository, ICrmCustomerRepository customers, ICrmContactRepository contacts, ICrmLeadRepository leads, ICrmOpportunityRepository opportunities) : ICrmTaskDomainService
{
    public async Task<SysCrmTask> CreateAsync(CrmTaskCreateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await EnsureRelatedRecordAsync(command.RelatedEntityType, command.RelatedEntityId, cancellationToken);
        var entity = new SysCrmTask { Status = CrmTaskStatus.Open };
        Apply(entity, command.RelatedEntityType, command.RelatedEntityId, command.Title, command.Description, command.DueAt, (int)CrmTaskStatus.Open, command.Priority, command.AssigneeUserId);
        return await repository.AddAsync(entity, cancellationToken);
    }
    public async Task<SysCrmTask> UpdateAsync(long id, CrmTaskUpdateCommand command, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        await EnsureRelatedRecordAsync(command.RelatedEntityType, command.RelatedEntityId, cancellationToken);
        var entity = await repository.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Task does not exist.");
        Apply(entity, command.RelatedEntityType, command.RelatedEntityId, command.Title, command.Description, command.DueAt, command.Status, command.Priority, command.AssigneeUserId);
        return await repository.UpdateAsync(entity, cancellationToken);
    }
    public async Task<SysCrmTask> ChangeStatusAsync(long id, int status, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (!Enum.IsDefined(typeof(CrmTaskStatus), status)) throw new ArgumentOutOfRangeException(nameof(status));
        var entity = await repository.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Task does not exist.");
        entity.Status = (CrmTaskStatus)status;
        entity.CompletedAt = entity.Status == CrmTaskStatus.Completed ? entity.CompletedAt ?? DateTime.UtcNow : null;
        return await repository.UpdateAsync(entity, cancellationToken);
    }
    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var entity = await repository.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Task does not exist.");
        if (!await repository.DeleteAsync(entity, cancellationToken)) throw new InvalidOperationException("Task deletion failed.");
    }
    private async Task EnsureRelatedRecordAsync(string? relatedType, long? relatedId, CancellationToken cancellationToken)
    {
        if (relatedType is null && relatedId is null) return;
        if (relatedType is null || relatedId is null || relatedId <= 0) throw new ArgumentException("Related entity type and positive id must be supplied together.");
        var exists = relatedType.Trim().ToLowerInvariant() switch
        {
            "customer" => await customers.GetByIdAsync(relatedId.Value, cancellationToken) is not null,
            "contact" => await contacts.GetByIdAsync(relatedId.Value, cancellationToken) is not null,
            "lead" => await leads.GetByIdAsync(relatedId.Value, cancellationToken) is not null,
            "opportunity" => await opportunities.GetByIdAsync(relatedId.Value, cancellationToken) is not null,
            _ => throw new ArgumentException("Related entity type must be customer, contact, lead or opportunity.", nameof(relatedType))
        };
        if (!exists) throw new InvalidOperationException("Related CRM record does not exist in the current tenant.");
    }
    private static void Apply(SysCrmTask entity, string? relatedType, long? relatedId, string title, string? description, DateTime? dueAt, int status, int priority, long? assignee)
    {
        if ((relatedType is null) != (relatedId is null)) throw new ArgumentException("Related entity type and id must be supplied together.");
        if (relatedId is <= 0) throw new ArgumentOutOfRangeException(nameof(relatedId));
        var normalizedType = string.IsNullOrWhiteSpace(relatedType) ? null : relatedType.Trim().ToLowerInvariant();
        if (normalizedType?.Length > 40) throw new ArgumentOutOfRangeException(nameof(relatedType));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var normalizedTitle = title.Trim();
        if (normalizedTitle.Length > 200) throw new ArgumentOutOfRangeException(nameof(title));
        if (!Enum.IsDefined(typeof(CrmTaskStatus), status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (priority is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(priority));
        if (assignee is <= 0) throw new ArgumentOutOfRangeException(nameof(assignee));
        entity.RelatedEntityType = normalizedType;
        entity.RelatedEntityId = relatedId;
        entity.Title = normalizedTitle;
        entity.Description = description;
        entity.DueAt = dueAt;
        entity.Status = (CrmTaskStatus)status;
        entity.Priority = priority;
        entity.AssigneeUserId = assignee;
        entity.CompletedAt = entity.Status == CrmTaskStatus.Completed ? entity.CompletedAt ?? DateTime.UtcNow : null;
    }
}