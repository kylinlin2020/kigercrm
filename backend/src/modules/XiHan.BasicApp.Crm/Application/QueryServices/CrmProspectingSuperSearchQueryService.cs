// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.Mvc;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

/// <summary>
/// Exposes the existing deterministic planner as a side-effect-free Dynamic API preview. This is a
/// pure function service: it holds no repositories, writes nothing and creates no Run, so tenant
/// isolation is enforced at the pipeline level by permission (the plan contains no tenant data).
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Prospecting")]
public sealed class CrmProspectingSuperSearchQueryService : CrmApplicationService, ICrmProspectingSuperSearchQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    [HttpPost]
    public Task<CrmProspectingSuperSearchPreviewDto> PreviewSuperSearchAsync(
        CrmProspectingSuperSearchPreviewInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(typeof(CrmProspectingSuperSearchDepth), input.Depth))
            throw new ArgumentOutOfRangeException(nameof(input.Depth));

        var plan = CrmProspectingSuperSearchPlanner.Create(new CrmProspectingSuperSearchInput(
            input.Products,
            input.Markets,
            input.CustomerTypes,
            input.Industries,
            input.ProviderCodes,
            input.ExcludedTerms,
            input.ExcludedDomains,
            (CrmProspectingSuperSearchDepth)input.Depth,
            input.RequestedRounds));
        return Task.FromResult(CrmProspectingMapper.ToSuperSearchPreviewDto(plan));
    }

    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    [HttpPost]
    public Task<CrmProspectingSuperSearchObjectiveParseResultDto> ParseSuperSearchObjectiveAsync(
        CrmProspectingSuperSearchObjectiveParseDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var result = CrmProspectingObjectiveParser.Parse(input.Objective);
        return Task.FromResult(CrmProspectingMapper.ToSuperSearchObjectiveParseResultDto(result));
    }
}
