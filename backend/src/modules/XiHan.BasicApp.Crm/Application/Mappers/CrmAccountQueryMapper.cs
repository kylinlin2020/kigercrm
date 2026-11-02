using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Application.Mappers;

public static class CrmAccountQueryMapper
{
    public static CrmCustomerListItemDto ToCustomerListItem(SysCrmCustomer entity) => new()
    {
        BasicId = entity.BasicId,
        Name = entity.Name,
        Website = entity.Website,
        CountryRegion = entity.CountryRegion,
        Industry = entity.Industry,
        OwnerUserId = entity.OwnerUserId,
        CreatedTime = entity.CreatedTime
    };

    public static CrmCustomerDetailDto ToCustomerDetail(SysCrmCustomer entity) => new()
    {
        BasicId = entity.BasicId,
        Name = entity.Name,
        LegalName = entity.LegalName,
        Website = entity.Website,
        CountryRegion = entity.CountryRegion,
        Industry = entity.Industry,
        Source = entity.Source,
        OwnerUserId = entity.OwnerUserId,
        Notes = entity.Notes,
        CreatedTime = entity.CreatedTime
    };

    public static CrmContactListItemDto ToContactListItem(SysCrmContact entity) => new()
    {
        BasicId = entity.BasicId,
        CustomerId = entity.CustomerId,
        FullName = string.Join(" ", new[] { entity.FirstName, entity.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
        JobTitle = entity.JobTitle,
        Email = entity.Email,
        Phone = entity.Phone,
        IsPrimary = entity.IsPrimary,
        OwnerUserId = entity.OwnerUserId,
        CreatedTime = entity.CreatedTime
    };
}
