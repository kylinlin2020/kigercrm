// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.Mvc;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.Extensions;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Domain.Shared.Paging.Dtos;
using XiHan.Framework.Domain.Shared.Paging.Enums;
using XiHan.Framework.Domain.Shared.Paging.Models;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

/// <summary>
/// Paginated read of one Run's candidate review queue under the same record authorization as
/// every other prospecting read. Replaces the former fixed 500-row RunCandidates projection:
/// page metadata comes from the request, filters are composed only when actually present, and
/// the ordering is stable (CreatedTime then BasicId) so paging never skips or repeats a row.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Prospecting")]
public sealed class CrmProspectingCandidateQueryService(
    ICrmProspectingRunRepository runs,
    ICrmProspectingCampaignRepository campaigns,
    ICrmProspectingCandidateRepository candidates,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmProspectingCandidateQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    [HttpPost]
    public async Task<PageResultDtoBase<CrmProspectingCandidateDto>> GetPageAsync(
        CrmProspectingCandidatePageQueryDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.RunId <= 0) throw new ArgumentOutOfRangeException(nameof(input.RunId));

        var run = await runs.GetByIdAsync(input.RunId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        var campaign = await campaigns.GetByIdAsync(run.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);

        var request = new BasicAppPRDto { Page = input.Page, Conditions = new QueryConditions() };
        request.Conditions.AddFilter((SysCrmProspectingCandidate candidate) => candidate.RunId, input.RunId);
        if (input.Status.HasValue)
        {
            if (!Enum.IsDefined(typeof(CrmProspectingCandidateStatus), input.Status.Value))
                throw new ArgumentOutOfRangeException(nameof(input.Status));
            request.Conditions.AddFilter(
                (SysCrmProspectingCandidate candidate) => candidate.Status,
                (CrmProspectingCandidateStatus)input.Status.Value);
        }
        if (input.Shortlisted.HasValue)
        {
            // Tri-state shortlist filter: the timestamp column itself is the marker, so the
            // filter translates to an IS NULL / IS NOT NULL predicate on Shortlisted_At.
            request.Conditions.AddFilter<SysCrmProspectingCandidate, DateTimeOffset?>(
                candidate => candidate.ShortlistedAt,
                null,
                input.Shortlisted.Value ? QueryOperator.IsNotNull : QueryOperator.IsNull);
        }

        // Stable ordering: newest first with BasicId as the tie-breaker, so consecutive pages
        // partition the set without repeating or dropping rows even when CreatedTime collides.
        request.Conditions.AddSort((SysCrmProspectingCandidate candidate) => candidate.CreatedTime, SortDirection.Descending, 0);
        request.Conditions.AddSort((SysCrmProspectingCandidate candidate) => candidate.BasicId, SortDirection.Descending, 1);

        var page = await candidates.GetPagedAsync(request, cancellationToken);
        var items = page.Items.Select(CrmProspectingMapper.ToCandidateDto).ToList();
        return new PageResultDtoBase<CrmProspectingCandidateDto>(items, page.Page) { ExtendDatas = page.ExtendDatas };
    }
}
