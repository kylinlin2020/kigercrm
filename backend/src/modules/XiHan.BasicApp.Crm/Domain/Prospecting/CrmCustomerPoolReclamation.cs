using XiHan.BasicApp.Crm.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Shared idle-claim reclamation used by both the manual API and the scheduled worker. Recovering
/// twice is harmless: each entry is guarded by the repository's conditional update.
/// </summary>
public static class CrmCustomerPoolReclamation
{
    public const int MaxBatch = 100;

    public static async Task<int> RecoverIdleAsync(
        ICrmCustomerPoolRepository entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var now = DateTimeOffset.UtcNow;
        var threshold = now.Add(-CrmCustomerPoolPolicy.ClaimIdleTimeout);
        var idle = await entries.GetIdleClaimsAsync(threshold, MaxBatch, cancellationToken);
        var recovered = 0;
        foreach (var entry in idle)
        {
            if (await entries.TryRecoverAsync(entry.BasicId, threshold, now, cancellationToken))
                recovered++;
        }
        return recovered;
    }
}
