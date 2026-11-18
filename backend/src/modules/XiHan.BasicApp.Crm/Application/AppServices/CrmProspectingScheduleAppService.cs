// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Security.Users;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>
/// Approval lifecycle for recurring prospecting schedules. Dispatch is deliberately NOT exposed
/// here: only the worker calls the dispatcher, so no client can create a Run outside the schedule's
/// approved cadence.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Prospecting")]
public sealed class CrmProspectingScheduleAppService(
    ICrmProspectingCampaignRepository campaigns,
    ICrmProspectingScheduleRepository schedules,
    IUserDataScopeFilterService dataScope,
    ICurrentUser currentUser)
    : CrmApplicationService, ICrmProspectingScheduleAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmProspectingScheduleDto> CreateScheduleAsync(
        CrmProspectingScheduleCreateDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.CampaignId <= 0) throw new ArgumentOutOfRangeException(nameof(input.CampaignId));
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RequestKeyPrefix);
        // <= 80 leaves room for ':' + 15-digit occurrence timestamp within the Run's 100-char key.
        if (input.RequestKeyPrefix.Length > 80) throw new ArgumentOutOfRangeException(nameof(input.RequestKeyPrefix));
        if (!Enum.IsDefined(typeof(CrmProspectingScheduleFrequency), input.Frequency))
            throw new ArgumentOutOfRangeException(nameof(input.Frequency));

        var campaign = await campaigns.GetByIdAsync(input.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);

        var existing = await schedules.GetByRequestKeyAsync(input.RequestKeyPrefix, cancellationToken);
        if (existing is not null)
        {
            if (existing.CampaignId != input.CampaignId || !string.Equals(existing.Name, input.Name.Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException("The schedule request key prefix was already used for a different schedule.");
            return CrmProspectingMapper.ToScheduleDto(existing);
        }

        var entity = new SysCrmProspectingSchedule
        {
            CampaignId = input.CampaignId,
            Name = input.Name.Trim(),
            RequestKeyPrefix = input.RequestKeyPrefix.Trim(),
            TimeZoneId = input.TimeZoneId.Trim(),
            Frequency = (CrmProspectingScheduleFrequency)input.Frequency,
            Interval = input.Interval,
            TimeOfDayMinutes = input.TimeOfDayMinutes,
            DayOfWeek = input.DayOfWeek,
            DayOfMonth = input.DayOfMonth,
            StartDateUtc = input.StartDateUtc,
            EndDateUtc = input.EndDateUtc,
            Status = CrmProspectingScheduleStatus.Draft
        };
        // Validate the recurrence contract at creation time; the first instant is computed on approval.
        _ = ComputeNext(entity, input.StartDateUtc.AddTicks(-1));
        return CrmProspectingMapper.ToScheduleDto(await schedules.AddAsync(entity, cancellationToken));
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmProspectingScheduleDto>> GetSchedulesAsync(
        long campaignId,
        CancellationToken cancellationToken = default)
    {
        if (campaignId <= 0) throw new ArgumentOutOfRangeException(nameof(campaignId));
        var campaign = await campaigns.GetByIdAsync(campaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
        var rows = await schedules.GetListAsync(schedule => schedule.CampaignId == campaignId, cancellationToken);
        return rows.Select(CrmProspectingMapper.ToScheduleDto).ToArray();
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingScheduleDto> SubmitScheduleForApprovalAsync(
        long scheduleId,
        CancellationToken cancellationToken = default)
    {
        var schedule = await GetAccessibleAsync(scheduleId, cancellationToken);
        if (schedule.Status != CrmProspectingScheduleStatus.Draft)
            throw new InvalidOperationException("Only a draft schedule can be submitted for approval.");
        schedule.Status = CrmProspectingScheduleStatus.PendingApproval;
        return CrmProspectingMapper.ToScheduleDto(await schedules.UpdateAsync(schedule, cancellationToken));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingScheduleDto> DecideScheduleAsync(
        CrmProspectingScheduleDecisionDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var schedule = await GetAccessibleAsync(input.ScheduleId, cancellationToken);
        if (schedule.Status != CrmProspectingScheduleStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending schedule can be decided.");

        if (!input.Approve)
        {
            schedule.Status = CrmProspectingScheduleStatus.Rejected;
        }
        else
        {
            schedule.Status = CrmProspectingScheduleStatus.Approved;
            schedule.ApprovedBy = currentUser.UserId;
            schedule.ApprovedAt = DateTimeOffset.UtcNow;
            schedule.NextRunAtUtc = ComputeNext(schedule, DateTimeOffset.UtcNow);
        }
        return CrmProspectingMapper.ToScheduleDto(await schedules.UpdateAsync(schedule, cancellationToken));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingScheduleDto> PauseScheduleAsync(long scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await GetAccessibleAsync(scheduleId, cancellationToken);
        if (schedule.Status is not (CrmProspectingScheduleStatus.Approved or CrmProspectingScheduleStatus.Paused))
            throw new InvalidOperationException("Only an approved or paused schedule can be paused.");
        schedule.Status = CrmProspectingScheduleStatus.Paused;
        schedule.NextRunAtUtc = null;
        return CrmProspectingMapper.ToScheduleDto(await schedules.UpdateAsync(schedule, cancellationToken));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingScheduleDto> ResumeScheduleAsync(long scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await GetAccessibleAsync(scheduleId, cancellationToken);
        if (schedule.Status != CrmProspectingScheduleStatus.Paused)
            throw new InvalidOperationException("Only a paused schedule can be resumed.");
        schedule.Status = CrmProspectingScheduleStatus.Approved;
        schedule.NextRunAtUtc = ComputeNext(schedule, DateTimeOffset.UtcNow);
        return CrmProspectingMapper.ToScheduleDto(await schedules.UpdateAsync(schedule, cancellationToken));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingScheduleDto> CancelScheduleAsync(long scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await GetAccessibleAsync(scheduleId, cancellationToken);
        if (schedule.Status is CrmProspectingScheduleStatus.Cancelled or CrmProspectingScheduleStatus.Rejected)
            throw new InvalidOperationException("The schedule is already terminal.");
        schedule.Status = CrmProspectingScheduleStatus.Cancelled;
        schedule.NextRunAtUtc = null;
        return CrmProspectingMapper.ToScheduleDto(await schedules.UpdateAsync(schedule, cancellationToken));
    }

    private async Task<SysCrmProspectingSchedule> GetAccessibleAsync(long scheduleId, CancellationToken cancellationToken)
    {
        if (scheduleId <= 0) throw new ArgumentOutOfRangeException(nameof(scheduleId));
        var schedule = await schedules.GetByIdAsync(scheduleId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting schedule does not exist.");
        var campaign = await campaigns.GetByIdAsync(schedule.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
        return schedule;
    }

    internal static DateTimeOffset? ComputeNext(SysCrmProspectingSchedule schedule, DateTimeOffset notBeforeUtc)
        => CrmProspectingScheduleRecurrence.NextOccurrence(
            notBeforeUtc,
            schedule.StartDateUtc,
            schedule.EndDateUtc,
            schedule.TimeZoneId,
            schedule.Frequency,
            schedule.Interval,
            schedule.TimeOfDayMinutes,
            schedule.DayOfWeek,
            schedule.DayOfMonth);
}
