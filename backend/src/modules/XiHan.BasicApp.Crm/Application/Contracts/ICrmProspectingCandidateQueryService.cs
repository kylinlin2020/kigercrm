// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Read-only paginated candidate review queue for one Run.</summary>
public interface ICrmProspectingCandidateQueryService : IApplicationService
{
    Task<PageResultDtoBase<CrmProspectingCandidateDto>> GetPageAsync(
        CrmProspectingCandidatePageQueryDto input, CancellationToken cancellationToken = default);
}
