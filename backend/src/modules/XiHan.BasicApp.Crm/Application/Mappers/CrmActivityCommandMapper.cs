using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;
namespace XiHan.BasicApp.Crm.Application.Mappers;
public static class CrmActivityCommandMapper
{
    public static CrmActivityCommand ToCommand(CrmActivityCreateDto input) => new(input.RelatedEntityType, input.RelatedEntityId, input.ActivityType, input.Subject, input.Content, input.OccurredAt, input.OwnerUserId, input.ExternalSystem, input.ExternalId);
    public static CrmActivityCommand ToCommand(CrmActivityUpdateDto input) => new(input.RelatedEntityType, input.RelatedEntityId, input.ActivityType, input.Subject, input.Content, input.OccurredAt, input.OwnerUserId, input.ExternalSystem, input.ExternalId);
}