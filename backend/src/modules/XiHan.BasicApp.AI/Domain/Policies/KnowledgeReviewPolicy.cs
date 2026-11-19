using System.Security.Cryptography;
using System.Text;
using XiHan.BasicApp.AI.Domain.Enums;

namespace XiHan.BasicApp.AI.Domain.Policies;

/// <summary>
/// Deterministic review gating: a document needs a decision only on first submission or after its
/// raw content changed. Rejection always requires an explicit reason.
/// </summary>
public static class KnowledgeReviewPolicy
{
    public static string HashContent(string rawContent)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawContent ?? string.Empty))).ToLowerInvariant();

    public static (bool Allowed, string Reason) CanSubmit(KnowledgeReviewStatus? latestStatus, string currentHash, string? lastApprovedHash)
    {
        if (latestStatus is null) return (true, "first_review");
        if (currentHash != lastApprovedHash) return (true, "content_changed");
        if (latestStatus == KnowledgeReviewStatus.Rejected) return (false, "rejected_unchanged");
        return (false, "already_reviewed_unchanged");
    }

    public static (bool Allowed, string Reason) CanDecide(KnowledgeReviewStatus current, bool approve, string? comment)
    {
        if (current != KnowledgeReviewStatus.Pending) return (false, "not_pending");
        if (!approve && string.IsNullOrWhiteSpace(comment)) return (false, "reject_reason_required");
        return (true, approve ? "approved" : "rejected");
    }
}
