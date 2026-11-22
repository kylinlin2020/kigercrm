using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Security.Users;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>
/// Renders outreach templates from whitelisted candidate fields and gates every draft behind an
/// explicit human decision. No message is sent and no external provider is contacted.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "OutreachDraft")]
public sealed class CrmOutreachDraftAppService(
    ICrmOutreachDraftRepository drafts,
    ICrmOutreachEnrollmentRepository enrollments,
    ICrmOutreachSequenceRepository sequences,
    ICrmProspectingCandidateRepository candidates,
    ICurrentUser currentUser)
    : CrmApplicationService, ICrmOutreachDraftAppService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<string> PreviewAsync(
        CrmOutreachDraftPreviewDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var candidate = await GetCandidateAsync(input.CandidateId, cancellationToken);
        return Render(input.Template, candidate);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmOutreachDraftDto> CreateAsync(
        CrmOutreachDraftCreateDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var enrollment = await enrollments.GetByIdAsync(input.EnrollmentId, cancellationToken)
            ?? throw new InvalidOperationException("Enrollment does not exist.");
        if (enrollment.Status is CrmOutreachEnrollmentStatus.Suppressed or CrmOutreachEnrollmentStatus.Completed)
            throw new InvalidOperationException("A suppressed or completed enrollment cannot create drafts.");

        var sequence = await sequences.GetByIdAsync(enrollment.SequenceId, cancellationToken)
            ?? throw new InvalidOperationException("Sequence does not exist.");
        var steps = CrmOutreachPolicy.ParseSteps(sequence.StepsJson);
        if (input.Step is < 0 || input.Step >= steps.Count)
            throw new ArgumentOutOfRangeException(nameof(input), "Step is outside the sequence plan.");
        var step = steps[input.Step];
        var candidate = await GetCandidateAsync(enrollment.CandidateId, cancellationToken);
        var rendered = Render(step.Template, candidate);

        var draft = await drafts.AddAsync(new SysCrmOutreachDraft
        {
            EnrollmentId = enrollment.BasicId,
            CandidateId = candidate.BasicId,
            Step = input.Step,
            Channel = (CrmOutreachChannel)step.Channel,
            RenderedText = rendered,
            ContentHash = CrmOutreachTemplateRenderer.ComputeHash(rendered),
            Status = CrmOutreachDraftStatus.Planned
        }, cancellationToken);
        return ToDto(draft);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmOutreachDraftDto>> ListAsync(
        long enrollmentId,
        CancellationToken cancellationToken = default)
    {
        var rows = await drafts.GetByEnrollmentAsync(enrollmentId, cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmOutreachDraftDto?> GetDraftAsync(
        long draftId,
        CancellationToken cancellationToken = default)
    {
        var draft = await drafts.GetByIdAsync(draftId, cancellationToken);
        return draft is null ? null : ToDto(draft);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmOutreachDraftDto> ApproveAsync(
        long draftId,
        CancellationToken cancellationToken = default)
    {
        var draft = await drafts.GetByIdForStateAsync(draftId, CrmOutreachDraftStatus.Planned, cancellationToken)
            ?? throw new InvalidOperationException("Draft is not pending review or does not exist.");
        if (!CrmOutreachDraftPolicy.CanApprove(draft.Status))
            throw new InvalidOperationException("Draft cannot be approved in its current state.");
        var now = DateTimeOffset.UtcNow;
        if (!await drafts.UpdateAsync(
                item => new SysCrmOutreachDraft
                {
                    Status = CrmOutreachDraftStatus.Approved,
                    ApprovedBy = currentUser.UserId,
                    ApprovedAt = now
                },
                item => item.BasicId == draftId && item.Status == CrmOutreachDraftStatus.Planned,
                cancellationToken))
            throw new InvalidOperationException("Draft was reviewed by another user; please retry.");
        return ToDto(await drafts.GetByIdAsync(draftId, cancellationToken)
            ?? throw new InvalidOperationException("Draft does not exist."));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmOutreachDraftDto> RejectAsync(
        CrmOutreachDraftReviewDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = CrmOutreachDraftPolicy.NormalizeReason(input.Reason);
        var draft = await drafts.GetByIdForStateAsync(input.DraftId, CrmOutreachDraftStatus.Planned, cancellationToken)
            ?? throw new InvalidOperationException("Draft is not pending review or does not exist.");
        if (!CrmOutreachDraftPolicy.CanReject(draft.Status))
            throw new InvalidOperationException("Draft cannot be rejected in its current state.");
        if (!await drafts.UpdateAsync(
                item => new SysCrmOutreachDraft
                {
                    Status = CrmOutreachDraftStatus.Rejected,
                    RejectionReason = reason
                },
                item => item.BasicId == input.DraftId && item.Status == CrmOutreachDraftStatus.Planned,
                cancellationToken))
            throw new InvalidOperationException("Draft was reviewed by another user; please retry.");
        return ToDto(await drafts.GetByIdAsync(input.DraftId, cancellationToken)
            ?? throw new InvalidOperationException("Draft does not exist."));
    }

    private async Task<SysCrmProspectingCandidate> GetCandidateAsync(
        long candidateId,
        CancellationToken cancellationToken)
        => await candidates.GetByIdAsync(candidateId, cancellationToken)
            ?? throw new InvalidOperationException("Candidate does not exist.");

    private static string Render(string template, SysCrmProspectingCandidate candidate)
        => CrmOutreachTemplateRenderer.Render(
            template, CrmOutreachTemplateRenderer.BuildVariables(candidate));

    private static CrmOutreachDraftDto ToDto(SysCrmOutreachDraft draft)
        => new()
        {
            BasicId = draft.BasicId,
            EnrollmentId = draft.EnrollmentId,
            CandidateId = draft.CandidateId,
            Step = draft.Step,
            Channel = (int)draft.Channel,
            RenderedText = draft.RenderedText,
            ContentHash = draft.ContentHash,
            Status = (int)draft.Status,
            ApprovedBy = draft.ApprovedBy,
            ApprovedAt = draft.ApprovedAt,
            RejectionReason = draft.RejectionReason
        };
}
