using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;

namespace XiHan.BasicApp.Crm.Application.Mappers;

public static class CrmLeadCommandMapper
{
    public static CrmLeadCreateCommand ToCommand(CrmLeadCreateDto input) =>
        new(input.CompanyName, input.ContactName, input.Email, input.Phone, input.Website, input.CountryRegion, input.Source, input.SourceReference, input.OwnerUserId, input.Notes);

    public static CrmLeadUpdateCommand ToCommand(CrmLeadUpdateDto input) =>
        new(input.BasicId, input.CompanyName, input.ContactName, input.Email, input.Phone, input.Website, input.CountryRegion, input.Source, input.SourceReference, input.Status, input.OwnerUserId, input.Notes);
}
