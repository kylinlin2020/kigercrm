// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Application.Mappers;

/// <summary>
/// Prospecting entity-to-DTO mappings used by application services and field-level security discovery.
/// </summary>
public static class CrmProspectingMapper
{
    /// <summary>Maps a prospecting candidate entity to its review-list DTO.</summary>
    public static CrmProspectingCandidateDto ToCandidateDto(SysCrmProspectingCandidate entity) => new()
    {
        BasicId = entity.BasicId,
        RunId = entity.RunId,
        ProviderCode = entity.ProviderCode,
        ExternalId = entity.ExternalId,
        Source = entity.Source,
        Title = entity.Title,
        CompanyName = entity.CompanyName,
        ContactName = entity.ContactName,
        BusinessEmail = entity.BusinessEmail,
        Website = entity.Website,
        Summary = entity.Summary,
        Status = (int)entity.Status,
        DuplicateReason = entity.DuplicateReason,
        ConvertedLeadId = entity.ConvertedLeadId,
        CreatedTime = entity.CreatedTime
    };
}
