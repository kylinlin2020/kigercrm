// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
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
/// Human-gated outreach management. The application never sends a message: a touchpoint starts as
/// Planned, an operator approves it, and only an explicit operator record can move it to Sent.
/// Replies and suppression feed a bounded effect-review projection.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Outreach")]
public sealed class CrmOutreachAppService(
    ICrmOutreachSequenceRepository sequences,
    ICrmOutreachEnrollmentRepository enrollments,
    ICrmOutreachTouchpointRepository touchpoints,
    ICrmProspectingCandidateRepository candidates,
    ICrmProspectingRunRepository runs,
    ICrmProspectingCampaignRepository campaigns,
    IUserDataScopeFilterService dataScope,
    ICurrentUser currentUser)
    : CrmApplicationService, ICrmOutreachAppService
{
    private static readonly JsonSerializerOptions StepJsonOptions = new(JsonSerializerDefaults.Web);

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmOutreachSequenceDto> CreateSequenceAsync(CrmOutreachSequenceCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
        var steps = CrmOutreachPolicy.ParseSteps(JsonSerializer.Serialize(input.Steps, StepJsonOptions));
        var existing = await sequences.GetByNameAsync(input.Name.Trim(), cancellationToken);
        if (existing is not null) return ToSequenceDto(existing, steps);

        var entity = new SysCrmOutreachSequence
        {
            Name = input.Name.Trim(),
            StepsJson = JsonSerializer.Serialize(steps.Select(step => new CrmOutreachStepDto
            {
                Channel = step.Channel,
                DelayDays = step.DelayDays,
                Template = step.Template
            }), StepJsonOptions),
            Status = CrmOutreachSequenceStatus.Draft
        };
        return ToSequenceDto(await sequences.AddAsync(entity, cancellationToken), steps);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmOutreachSequenceDto>> GetSequencesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await sequences.GetAllAsync(cancellationToken);
        return rows.Select(row => ToSequenceDto(row, CrmOutreachPolicy.ParseSteps(row.StepsJson))).ToArray();
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmOutreachSequenceDto> ActivateSequenceAsync(long sequenceId, CancellationToken cancellationToken = default)
    {
        var sequence = await GetSequenceAsync(sequenceId, cancellationToken);
        if (sequence.Status is CrmOutreachSequenceStatus.Archived)
            throw new InvalidOperationException("An archived sequence cannot be activated.");
        _ = CrmOutreachPolicy.ParseSteps(sequence.StepsJson);
        sequence.Status = CrmOutreachSequenceStatus.Active;
        sequence.ActivatedAt ??= DateTimeOffset.UtcNow;
        return ToSequenceDto(await sequences.UpdateAsync(sequence, cancellationToken),
            CrmOutreachPolicy.ParseSteps(sequence.StepsJson));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmOutreachSequenceDto> PauseSequenceAsync(long sequenceId, CancellationToken cancellationToken = default)
    {
        var sequence = await GetSequenceAsync(sequenceId, cancellationToken);
        if (sequence.Status != CrmOutreachSequenceStatus.Active)
            throw new InvalidOperationException("Only an active sequence can be paused.");
        sequence.Status = CrmOutreachSequenceStatus.Paused;
        return ToSequenceDto(await sequences.UpdateAsync(sequence, cancellationToken),
            CrmOutreachPolicy.ParseSteps(sequence.StepsJson));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmOutreachEnrollmentDto> EnrollCandidateAsync(CrmOutreachEnrollDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sequence = await GetSequenceAsync(input.SequenceId, cancellationToken);
        if (sequence.Status != CrmOutreachSequenceStatus.Active)
            throw new InvalidOperationException("Only an active sequence can enroll candidates.");
        var steps = CrmOutreachPolicy.ParseSteps(sequence.StepsJson);
        var candidate = await EnsureCandidateAccessibleAsync(input.CandidateId, cancellationToken);
        // Include terminal history: a suppressed candidate must not be silently re-enrolled.
        var existing = (await enrollments.GetByCandidateAsync(input.CandidateId, cancellationToken)).FirstOrDefault();
        if (existing is not null && existing.SequenceId == sequence.BasicId
            && existing.Status is CrmOutreachEnrollmentStatus.Active or CrmOutreachEnrollmentStatus.Paused)
            return ToEnrollmentDto(existing);

        var eligibility = CrmOutreachPolicy.EvaluateEligibility(candidate, existing, steps);
        if (!eligibility.Eligible)
            throw new InvalidOperationException($"Candidate is not eligible for outreach: {eligibility.ReasonCode}.");

        var enrollment = await enrollments.AddAsync(new SysCrmOutreachEnrollment
        {
            SequenceId = sequence.BasicId,
            CandidateId = candidate.BasicId,
            Status = CrmOutreachEnrollmentStatus.Active,
            CurrentStep = 0,
            NextActionAtUtc = DateTimeOffset.UtcNow.AddDays(steps[0].DelayDays)
        }, cancellationToken);
        await touchpoints.AddAsync(new SysCrmOutreachTouchpoint
        {
            EnrollmentId = enrollment.BasicId,
            CandidateId = candidate.BasicId,
            Step = 0,
            Channel = (CrmOutreachChannel)steps[0].Channel,
            State = CrmOutreachTouchpointState.Planned,
            ContentHash = Hash(steps[0].Template)
        }, cancellationToken);
        return ToEnrollmentDto(enrollment);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmOutreachEnrollmentDto>> GetEnrollmentsAsync(long sequenceId, CancellationToken cancellationToken = default)
    {
        _ = await GetSequenceAsync(sequenceId, cancellationToken);
        var rows = await enrollments.GetBySequenceAsync(sequenceId, cancellationToken);
        return rows.Select(ToEnrollmentDto).ToArray();
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmOutreachTouchpointDto>> GetTouchpointsAsync(long enrollmentId, CancellationToken cancellationToken = default)
    {
        var enrollment = await GetEnrollmentAsync(enrollmentId, cancellationToken);
        await EnsureCandidateAccessibleAsync(enrollment.CandidateId, cancellationToken);
        var rows = await touchpoints.GetByEnrollmentAsync(enrollmentId, cancellationToken);
        return rows.Select(ToTouchpointDto).ToArray();
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmOutreachTouchpointDto> ApproveTouchpointAsync(long touchpointId, CancellationToken cancellationToken = default)
    {
        var touchpoint = await touchpoints.GetByIdForStateAsync(touchpointId, CrmOutreachTouchpointState.Planned, cancellationToken)
            ?? throw new InvalidOperationException("Only a planned touchpoint can be approved.");
        var enrollment = await GetEnrollmentAsync(touchpoint.EnrollmentId, cancellationToken);
        await EnsureCandidateAccessibleAsync(enrollment.CandidateId, cancellationToken);
        touchpoint.State = CrmOutreachTouchpointState.Approved;
        touchpoint.ApprovedBy = currentUser.UserId;
        touchpoint.ApprovedAt = DateTimeOffset.UtcNow;
        return ToTouchpointDto(await touchpoints.UpdateAsync(touchpoint, cancellationToken));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmOutreachTouchpointDto> RecordTouchpointAsync(CrmOutreachRecordDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.State is not ((int)CrmOutreachTouchpointState.Sent
            or (int)CrmOutreachTouchpointState.Replied
            or (int)CrmOutreachTouchpointState.Bounced))
            throw new ArgumentOutOfRangeException(nameof(input.State), "A recorded touchpoint must be Sent, Replied or Bounced.");

        var touchpoint = await touchpoints.GetByIdForStateAsync(input.TouchpointId, CrmOutreachTouchpointState.Approved, cancellationToken)
            ?? throw new InvalidOperationException("Only an approved touchpoint can be recorded as sent.");
        var enrollment = await GetEnrollmentAsync(touchpoint.EnrollmentId, cancellationToken);
        await EnsureCandidateAccessibleAsync(enrollment.CandidateId, cancellationToken);
        var sequence = await GetSequenceAsync(enrollment.SequenceId, cancellationToken);
        var steps = CrmOutreachPolicy.ParseSteps(sequence.StepsJson);

        touchpoint.State = (CrmOutreachTouchpointState)input.State;
        touchpoint.ReplyClass = input.State == (int)CrmOutreachTouchpointState.Replied
            ? CrmOutreachPolicy.ClassifyReply(input.ReplyText ?? string.Empty)
            : CrmOutreachReplyClass.None;
        touchpoint.OccurredAtUtc = DateTimeOffset.UtcNow;
        var saved = await touchpoints.UpdateAsync(touchpoint, cancellationToken);

        var next = CrmOutreachPolicy.NextAction(enrollment, steps, touchpoint.ReplyClass, DateTimeOffset.UtcNow);
        enrollment.CurrentStep = next.NextStep;
        enrollment.Status = next.Status;
        enrollment.NextActionAtUtc = next.NextActionAtUtc;
        enrollment.SuppressionReason = next.SuppressionReason;
        await enrollments.UpdateAsync(enrollment, cancellationToken);

        if (next.Status == CrmOutreachEnrollmentStatus.Active && next.NextActionAtUtc is not null)
        {
            var step = steps[next.NextStep];
            await touchpoints.AddAsync(new SysCrmOutreachTouchpoint
            {
                EnrollmentId = enrollment.BasicId,
                CandidateId = enrollment.CandidateId,
                Step = next.NextStep,
                Channel = (CrmOutreachChannel)step.Channel,
                State = CrmOutreachTouchpointState.Planned,
                ContentHash = Hash(step.Template)
            }, cancellationToken);
        }
        if (next.Status == CrmOutreachEnrollmentStatus.Suppressed)
            await SuppressPendingAsync(enrollment.BasicId, "reply_not_interested", cancellationToken);
        return ToTouchpointDto(saved);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmOutreachEnrollmentDto> SuppressEnrollmentAsync(long enrollmentId, string reason, CancellationToken cancellationToken = default)
    {
        var normalizedReason = CrmOutreachDraftPolicy.NormalizeReason(reason);
        var enrollment = await GetEnrollmentAsync(enrollmentId, cancellationToken);
        await EnsureCandidateAccessibleAsync(enrollment.CandidateId, cancellationToken);
        enrollment.Status = CrmOutreachEnrollmentStatus.Suppressed;
        enrollment.SuppressionReason = normalizedReason;
        enrollment.NextActionAtUtc = null;
        var saved = await enrollments.UpdateAsync(enrollment, cancellationToken);
        await SuppressPendingAsync(enrollmentId, enrollment.SuppressionReason, cancellationToken);
        return ToEnrollmentDto(saved);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmOutreachReviewDto> GetReviewAsync(long sequenceId, CancellationToken cancellationToken = default)
    {
        _ = await GetSequenceAsync(sequenceId, cancellationToken);
        var rows = await enrollments.GetBySequenceAsync(sequenceId, cancellationToken);
        var review = new CrmOutreachReviewDto { SequenceId = sequenceId, Enrollments = rows.Count };
        foreach (var enrollment in rows)
        {
            foreach (var touchpoint in await touchpoints.GetByEnrollmentAsync(enrollment.BasicId, cancellationToken))
            {
                switch (touchpoint.State)
                {
                    case CrmOutreachTouchpointState.Planned: review.Planned++; break;
                    case CrmOutreachTouchpointState.Approved: review.Approved++; break;
                    case CrmOutreachTouchpointState.Sent: review.Sent++; break;
                    case CrmOutreachTouchpointState.Replied: review.Replied++; break;
                    case CrmOutreachTouchpointState.Bounced: review.Bounced++; break;
                    case CrmOutreachTouchpointState.Suppressed: review.Suppressed++; break;
                }
                if (touchpoint.ReplyClass == CrmOutreachReplyClass.Interested) review.Interested++;
                if (touchpoint.ReplyClass == CrmOutreachReplyClass.NotInterested) review.NotInterested++;
            }
        }
        return review;
    }

    private async Task SuppressPendingAsync(long enrollmentId, string reason, CancellationToken cancellationToken)
    {
        foreach (var pending in await touchpoints.GetByEnrollmentAsync(enrollmentId, cancellationToken))
        {
            if (pending.State is not (CrmOutreachTouchpointState.Planned or CrmOutreachTouchpointState.Approved))
                continue;
            pending.State = CrmOutreachTouchpointState.Suppressed;
            await touchpoints.UpdateAsync(pending, cancellationToken);
        }
    }

    private async Task<SysCrmOutreachSequence> GetSequenceAsync(long sequenceId, CancellationToken cancellationToken)
    {
        if (sequenceId <= 0) throw new ArgumentOutOfRangeException(nameof(sequenceId));
        return await sequences.GetByIdAsync(sequenceId, cancellationToken)
            ?? throw new InvalidOperationException("Outreach sequence does not exist.");
    }

    private async Task<SysCrmOutreachEnrollment> GetEnrollmentAsync(long enrollmentId, CancellationToken cancellationToken)
    {
        if (enrollmentId <= 0) throw new ArgumentOutOfRangeException(nameof(enrollmentId));
        return await enrollments.GetByIdAsync(enrollmentId, cancellationToken)
            ?? throw new InvalidOperationException("Outreach enrollment does not exist.");
    }

    private async Task<SysCrmProspectingCandidate> EnsureCandidateAccessibleAsync(long candidateId, CancellationToken cancellationToken)
    {
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));
        var candidate = await candidates.GetByIdAsync(candidateId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting candidate does not exist.");
        var run = await runs.GetByIdAsync(candidate.RunId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        var campaign = await campaigns.GetByIdAsync(run.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
        return candidate;
    }

    private static string Hash(string template)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(template))).ToLowerInvariant();

    private static CrmOutreachSequenceDto ToSequenceDto(SysCrmOutreachSequence entity, IReadOnlyList<CrmOutreachStep> steps) => new()
    {
        BasicId = entity.BasicId,
        Name = entity.Name,
        Steps = steps.Select(step => new CrmOutreachStepDto
        {
            Channel = step.Channel,
            DelayDays = step.DelayDays,
            Template = step.Template
        }).ToArray(),
        Status = (int)entity.Status,
        ActivatedAt = entity.ActivatedAt,
        CreatedTime = entity.CreatedTime
    };

    private static CrmOutreachEnrollmentDto ToEnrollmentDto(SysCrmOutreachEnrollment entity) => new()
    {
        BasicId = entity.BasicId,
        SequenceId = entity.SequenceId,
        CandidateId = entity.CandidateId,
        Status = (int)entity.Status,
        CurrentStep = entity.CurrentStep,
        NextActionAtUtc = entity.NextActionAtUtc,
        SuppressionReason = entity.SuppressionReason,
        CreatedTime = entity.CreatedTime
    };

    private static CrmOutreachTouchpointDto ToTouchpointDto(SysCrmOutreachTouchpoint entity) => new()
    {
        BasicId = entity.BasicId,
        EnrollmentId = entity.EnrollmentId,
        CandidateId = entity.CandidateId,
        Step = entity.Step,
        Channel = (int)entity.Channel,
        State = (int)entity.State,
        ReplyClass = (int)entity.ReplyClass,
        ContentHash = entity.ContentHash,
        ApprovedBy = entity.ApprovedBy,
        ApprovedAt = entity.ApprovedAt,
        OccurredAtUtc = entity.OccurredAtUtc,
        CreatedTime = entity.CreatedTime
    };
}
