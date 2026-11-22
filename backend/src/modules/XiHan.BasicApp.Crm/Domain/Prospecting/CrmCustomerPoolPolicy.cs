using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Deterministic customer-pool rules. One customer has one pool entry; claiming is exclusive via
/// compare-and-swap in the repository, and idle claims are recovered after a bounded window.
/// </summary>
public static class CrmCustomerPoolPolicy
{
    public const int MaximumClaims = 10;
    public static readonly TimeSpan ClaimIdleTimeout = TimeSpan.FromDays(7);

    public static (bool Allowed, string Reason) CanClaim(SysCrmCustomerPoolEntry entry, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Status == CrmCustomerPoolStatus.Claimed && !IsIdle(entry, nowUtc))
            return (false, "already_claimed");
        if (entry.ClaimCount >= MaximumClaims)
            return (false, "claim_limit_reached");
        return (true, "ok");
    }

    public static (bool Allowed, string Reason) CanRelease(SysCrmCustomerPoolEntry entry, long userId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Status != CrmCustomerPoolStatus.Claimed) return (false, "not_claimed");
        if (entry.ClaimedUserId != userId) return (false, "not_claimant");
        return (true, "ok");
    }

    public static bool IsIdle(SysCrmCustomerPoolEntry entry, DateTimeOffset nowUtc)
        => entry.Status == CrmCustomerPoolStatus.Claimed
            && entry.ClaimedAtUtc.HasValue
            && nowUtc - entry.ClaimedAtUtc.Value > ClaimIdleTimeout;

    public static string? NormalizeNote(string? note)
        => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
