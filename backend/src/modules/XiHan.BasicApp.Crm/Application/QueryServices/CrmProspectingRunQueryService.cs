// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

/// <summary>
/// Read-only Run monitoring projection for the prospecting UI: the progress aggregate and the
/// durable per-provider execution checkpoints of one Run, under the same record authorization
/// as every other prospecting read.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Prospecting")]
public sealed class CrmProspectingRunQueryService(
    ICrmProspectingRunRepository runs,
    ICrmProspectingCampaignRepository campaigns,
    ICrmProspectingRunProviderExecutionRepository executions,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmProspectingRunQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmProspectingRunMonitorDto> GetRunMonitorAsync(long runId, CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        var run = await runs.GetByIdAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        var campaign = await campaigns.GetByIdAsync(run.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);

        var rows = await executions.GetByRunAsync(runId, cancellationToken);
        return new CrmProspectingRunMonitorDto
        {
            RunId = run.BasicId,
            Progress = CrmProspectingRunProgressBuilder.Build(rows),
            Executions = rows.Select(ToDto).ToArray()
        };
    }

    private static CrmProspectingRunProviderExecutionDto ToDto(SysCrmProspectingRunProviderExecution entity) => new()
    {
        BasicId = entity.BasicId,
        RunId = entity.RunId,
        PlanVersion = entity.PlanVersion,
        PlanOrdinal = entity.PlanOrdinal,
        ProviderCode = entity.ProviderCode,
        State = entity.State,
        AttemptCount = entity.AttemptCount,
        RawRecordCount = entity.RawRecordCount,
        ProjectedRecordCount = entity.ProjectedRecordCount,
        FailureCount = entity.FailureCount,
        FailureCode = entity.FailureCode,
        FailureSummary = entity.FailureSummary,
        StartedAt = entity.StartedAt,
        CompletedAt = entity.CompletedAt
    };
}
