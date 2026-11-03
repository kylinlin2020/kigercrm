using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
namespace XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;
public sealed class CrmActivityDomainService(ICrmActivityRepository repository) : ICrmActivityDomainService
{
    public async Task<SysCrmActivity> CreateAsync(CrmActivityCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var entity = new SysCrmActivity();
        Apply(entity, command);
        return await repository.AddAsync(entity, cancellationToken);
    }
    public async Task<SysCrmActivity> UpdateAsync(long id, CrmActivityCommand command, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var entity = await repository.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Activity does not exist.");
        Apply(entity, command);
        return await repository.UpdateAsync(entity, cancellationToken);
    }
    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var entity = await repository.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Activity does not exist.");
        if (!await repository.DeleteAsync(entity, cancellationToken)) throw new InvalidOperationException("Activity deletion failed.");
    }
    private static void Apply(SysCrmActivity entity, CrmActivityCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RelatedEntityType);
        var relatedType = command.RelatedEntityType.Trim().ToLowerInvariant();
        if (relatedType.Length > 40 || command.RelatedEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(command.RelatedEntityId));
        if (!Enum.IsDefined(typeof(CrmActivityType), command.ActivityType)) throw new ArgumentOutOfRangeException(nameof(command.ActivityType));
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Subject);
        var subject = command.Subject.Trim();
        if (subject.Length > 200) throw new ArgumentOutOfRangeException(nameof(command.Subject));
        if (command.OwnerUserId is <= 0) throw new ArgumentOutOfRangeException(nameof(command.OwnerUserId));
        if (command.ExternalSystem?.Length > 80 || command.ExternalId?.Length > 160) throw new ArgumentOutOfRangeException(nameof(command.ExternalId));
        entity.RelatedEntityType = relatedType;
        entity.RelatedEntityId = command.RelatedEntityId;
        entity.ActivityType = (CrmActivityType)command.ActivityType;
        entity.Subject = subject;
        entity.Content = command.Content;
        entity.OccurredAt = command.OccurredAt;
        entity.OwnerUserId = command.OwnerUserId;
        entity.ExternalSystem = string.IsNullOrWhiteSpace(command.ExternalSystem) ? null : command.ExternalSystem.Trim();
        entity.ExternalId = string.IsNullOrWhiteSpace(command.ExternalId) ? null : command.ExternalId.Trim();
    }
}