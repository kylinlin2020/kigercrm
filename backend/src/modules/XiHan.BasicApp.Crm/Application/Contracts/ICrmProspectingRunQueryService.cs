// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Read-only Run monitoring: progress aggregation and per-provider execution checkpoints.</summary>
public interface ICrmProspectingRunQueryService : IApplicationService
{
    Task<CrmProspectingRunMonitorDto> GetRunMonitorAsync(long runId, CancellationToken cancellationToken = default);
}
