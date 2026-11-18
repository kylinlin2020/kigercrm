using System.Text.Json;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

public sealed record CrmOutreachStep(int Channel, int DelayDays, string Template);

public sealed record CrmOutreachEligibility(bool Eligible, string ReasonCode);

public sealed record CrmOutreachNextAction(
    int NextStep,
    DateTimeOffset? NextActionAtUtc,
    CrmOutreachEnrollmentStatus Status,
    string? SuppressionReason);

/// <summary>
/// Deterministic outreach policy. It never sends anything: it validates frozen step plans, decides
/// whether a candidate may be enrolled, classifies replies and computes the next human action.
/// Suppression and explicit approval are first-class gates.
/// </summary>
public static class CrmOutreachPolicy
{
    private const int MaximumSteps = 20;

    private static readonly string[] InterestedTerms =
        ["interested", "please send", "more information", "quote", "有兴趣", "感兴趣", "请发", "报价"];
    private static readonly string[] NotInterestedTerms =
        ["not interested", "unsubscribe", "remove me", "stop", "不感兴趣", "不需要", "退订", "停止"];
    private static readonly string[] OutOfOfficeTerms =
        ["out of office", "on leave", "年假", "休假", "出差"];

    public static IReadOnlyList<CrmOutreachStep> ParseSteps(string stepsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepsJson);
        using var document = JsonDocument.Parse(stepsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The outreach sequence step plan must be a JSON array.");
        var steps = new List<CrmOutreachStep>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Each outreach step must be a JSON object.");
            var channel = item.GetProperty("channel").GetInt32();
            var delayDays = item.GetProperty("delayDays").GetInt32();
            var template = item.GetProperty("template").GetString() ?? string.Empty;
            if (!Enum.IsDefined(typeof(CrmOutreachChannel), channel))
                throw new ArgumentOutOfRangeException(nameof(stepsJson), "Unknown outreach channel.");
            if (delayDays is < 0 or > 365)
                throw new ArgumentOutOfRangeException(nameof(stepsJson), "Step delay must be between 0 and 365 days.");
            if (template.Length is 0 or > 2000 || template.Any(char.IsControl))
                throw new ArgumentException("Step template must be non-empty and within length limits.", nameof(stepsJson));
            steps.Add(new CrmOutreachStep(channel, delayDays, template));
        }
        if (steps.Count is < 1 or > MaximumSteps)
            throw new ArgumentOutOfRangeException(nameof(stepsJson), $"A sequence must have 1 to {MaximumSteps} steps.");
        return steps;
    }

    public static CrmOutreachEligibility EvaluateEligibility(
        SysCrmProspectingCandidate candidate,
        SysCrmOutreachEnrollment? existing,
        IReadOnlyList<CrmOutreachStep> steps)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(steps);
        if (candidate.Status is not (CrmProspectingCandidateStatus.Approved or CrmProspectingCandidateStatus.Converted))
            return new CrmOutreachEligibility(false, "candidate_not_qualified");
        if (existing is not null && existing.Status is CrmOutreachEnrollmentStatus.Active or CrmOutreachEnrollmentStatus.Paused)
            return new CrmOutreachEligibility(false, "already_enrolled");
        if (existing?.Status == CrmOutreachEnrollmentStatus.Suppressed)
            return new CrmOutreachEligibility(false, "suppressed");
        if (steps.Any(step => step.Channel == (int)CrmOutreachChannel.Email) && string.IsNullOrWhiteSpace(candidate.BusinessEmail))
            return new CrmOutreachEligibility(false, "email_required");
        return new CrmOutreachEligibility(true, "ok");
    }

    public static CrmOutreachReplyClass ClassifyReply(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return CrmOutreachReplyClass.None;
        var value = text.ToLowerInvariant();
        if (NotInterestedTerms.Any(term => value.Contains(term, StringComparison.Ordinal)))
            return CrmOutreachReplyClass.NotInterested;
        if (OutOfOfficeTerms.Any(term => value.Contains(term, StringComparison.Ordinal)))
            return CrmOutreachReplyClass.OutOfOffice;
        if (InterestedTerms.Any(term => value.Contains(term, StringComparison.Ordinal)))
            return CrmOutreachReplyClass.Interested;
        return CrmOutreachReplyClass.Unknown;
    }

    public static CrmOutreachNextAction NextAction(
        SysCrmOutreachEnrollment enrollment,
        IReadOnlyList<CrmOutreachStep> steps,
        CrmOutreachReplyClass replyClass,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        ArgumentNullException.ThrowIfNull(steps);
        if (replyClass == CrmOutreachReplyClass.NotInterested)
            return new CrmOutreachNextAction(enrollment.CurrentStep, null,
                CrmOutreachEnrollmentStatus.Suppressed, "reply_not_interested");

        var nextStep = enrollment.CurrentStep + 1;
        if (nextStep >= steps.Count)
            return new CrmOutreachNextAction(steps.Count, null, CrmOutreachEnrollmentStatus.Completed, null);

        var delay = replyClass == CrmOutreachReplyClass.Interested
            ? 1
            : replyClass == CrmOutreachReplyClass.OutOfOffice
                ? 7
                : steps[nextStep].DelayDays;
        // Out-of-office retries the same step; all other paths advance.
        var targetStep = replyClass == CrmOutreachReplyClass.OutOfOffice ? enrollment.CurrentStep : nextStep;
        return new CrmOutreachNextAction(targetStep, nowUtc.AddDays(delay), CrmOutreachEnrollmentStatus.Active, null);
    }
}
