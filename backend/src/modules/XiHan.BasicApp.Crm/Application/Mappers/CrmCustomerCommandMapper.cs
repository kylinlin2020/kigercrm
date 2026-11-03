using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;

namespace XiHan.BasicApp.Crm.Application.Mappers;

public static class CrmCustomerCommandMapper
{
    public static CrmCustomerCreateCommand ToCommand(CrmCustomerCreateDto input) =>
        new(input.Name, input.LegalName, input.Website, input.CountryRegion, input.Industry, input.Source, input.OwnerUserId, input.Notes);

    public static CrmCustomerUpdateCommand ToCommand(CrmCustomerUpdateDto input) =>
        new(input.BasicId, input.Name, input.LegalName, input.Website, input.CountryRegion, input.Industry, input.Source, input.OwnerUserId, input.Notes);
}
