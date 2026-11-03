using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;

namespace XiHan.BasicApp.Crm.Application.Mappers;

public static class CrmContactCommandMapper
{
    public static CrmContactCreateCommand ToCommand(CrmContactCreateDto input) =>
        new(input.CustomerId, input.FirstName, input.LastName, input.JobTitle, input.Email, input.Phone, input.WhatsApp, input.OwnerUserId, input.IsPrimary, input.Notes);

    public static CrmContactUpdateCommand ToCommand(CrmContactUpdateDto input) =>
        new(input.BasicId, input.CustomerId, input.FirstName, input.LastName, input.JobTitle, input.Email, input.Phone, input.WhatsApp, input.OwnerUserId, input.IsPrimary, input.Notes);
}
