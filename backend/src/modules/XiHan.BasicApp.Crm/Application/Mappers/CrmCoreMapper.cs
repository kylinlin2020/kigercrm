using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Application.Mappers;

/// <summary>Explicit persistence-to-response mappings used by field-security response masking.</summary>
public static class CrmCoreMapper
{
    public static CrmCustomerDto ToDto(SysCrmCustomer entity) => new()
    {
        BasicId = entity.BasicId,
        Name = entity.Name,
        LegalName = entity.LegalName,
        Website = entity.Website,
        CountryRegion = entity.CountryRegion,
        Industry = entity.Industry,
        Source = entity.Source,
        OwnerUserId = entity.OwnerUserId,
        Notes = entity.Notes
    };

    public static CrmContactDto ToDto(SysCrmContact entity) => new()
    {
        BasicId = entity.BasicId,
        CustomerId = entity.CustomerId,
        FirstName = entity.FirstName,
        LastName = entity.LastName,
        JobTitle = entity.JobTitle,
        Email = entity.Email,
        Phone = entity.Phone,
        WhatsApp = entity.WhatsApp,
        OwnerUserId = entity.OwnerUserId,
        IsPrimary = entity.IsPrimary,
        Notes = entity.Notes
    };

    public static CrmOpportunityDto ToDto(SysCrmOpportunity entity) => new()
    {
        BasicId = entity.BasicId,
        CustomerId = entity.CustomerId,
        PrimaryContactId = entity.PrimaryContactId,
        Name = entity.Name,
        Stage = (int)entity.Stage,
        Amount = entity.Amount,
        Currency = entity.Currency,
        ExpectedCloseDate = entity.ExpectedCloseDate,
        Probability = entity.Probability,
        OwnerUserId = entity.OwnerUserId,
        LostReason = entity.LostReason
    };

    public static CrmActivityDto ToDto(SysCrmActivity entity) => new()
    {
        BasicId = entity.BasicId,
        RelatedEntityType = entity.RelatedEntityType,
        RelatedEntityId = entity.RelatedEntityId,
        ActivityType = (int)entity.ActivityType,
        Subject = entity.Subject,
        Content = entity.Content,
        OccurredAt = entity.OccurredAt,
        OwnerUserId = entity.OwnerUserId,
        ExternalSystem = entity.ExternalSystem,
        ExternalId = entity.ExternalId
    };

    public static CrmTaskDto ToDto(SysCrmTask entity) => new()
    {
        BasicId = entity.BasicId,
        RelatedEntityType = entity.RelatedEntityType,
        RelatedEntityId = entity.RelatedEntityId,
        Title = entity.Title,
        Description = entity.Description,
        DueAt = entity.DueAt,
        Status = (int)entity.Status,
        Priority = entity.Priority,
        AssigneeUserId = entity.AssigneeUserId,
        CompletedAt = entity.CompletedAt
    };
}
