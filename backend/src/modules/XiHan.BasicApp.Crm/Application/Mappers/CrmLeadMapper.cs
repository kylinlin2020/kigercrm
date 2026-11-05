using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Application.Mappers;

/// <summary>Explicit projection prevents persistence-only fields leaking through APIs.</summary>
public static class CrmLeadMapper
{
    public static CrmLeadListItemDto ToListItemDto(SysCrmLead entity) => new()
    {
        BasicId = entity.BasicId,
        CompanyName = entity.CompanyName,
        ContactName = entity.ContactName,
        Email = entity.Email,
        Phone = entity.Phone,
        CountryRegion = entity.CountryRegion,
        Source = entity.Source,
        Status = (int)entity.Status,
        Score = entity.Score,
        OwnerUserId = entity.OwnerUserId,
        CreatedTime = entity.CreatedTime
    };

    public static CrmLeadDetailDto ToDetailDto(SysCrmLead entity) => new()
    {
        BasicId = entity.BasicId,
        CompanyName = entity.CompanyName,
        ContactName = entity.ContactName,
        Email = entity.Email,
        Phone = entity.Phone,
        CountryRegion = entity.CountryRegion,
        Source = entity.Source,
        Status = (int)entity.Status,
        Score = entity.Score,
        OwnerUserId = entity.OwnerUserId,
        CreatedTime = entity.CreatedTime,
        Website = entity.Website,
        SourceReference = entity.SourceReference,
        ConvertedCustomerId = entity.ConvertedCustomerId,
        ConvertedContactId = entity.ConvertedContactId,
        LastContactedAt = entity.LastContactedAt,
        Notes = entity.Notes
    };
}
