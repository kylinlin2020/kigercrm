using XiHan.BasicApp.Crm.Domain.Entities.Enums;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Explicit review transitions: only a planned draft can be decided, once.</summary>
public static class CrmOutreachDraftPolicy
{
    public static bool CanApprove(CrmOutreachDraftStatus status) => status == CrmOutreachDraftStatus.Planned;

    public static bool CanReject(CrmOutreachDraftStatus status) => status == CrmOutreachDraftStatus.Planned;

    public static string NormalizeReason(string? reason)
        => string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException("A rejection reason is required.", nameof(reason))
            : reason.Trim().Length > 500 ? reason.Trim()[..500] : reason.Trim();
}
