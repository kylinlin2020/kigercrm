using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;
namespace XiHan.BasicApp.Crm.Application.Mappers;
public static class CrmTaskCommandMapper
{
    public static CrmTaskCreateCommand ToCommand(CrmTaskCreateDto input) => new(input.RelatedEntityType, input.RelatedEntityId, input.Title, input.Description, input.DueAt, input.Priority, input.AssigneeUserId);
    public static CrmTaskUpdateCommand ToCommand(CrmTaskUpdateDto input) => new(input.RelatedEntityType, input.RelatedEntityId, input.Title, input.Description, input.DueAt, input.Status, input.Priority, input.AssigneeUserId);
}