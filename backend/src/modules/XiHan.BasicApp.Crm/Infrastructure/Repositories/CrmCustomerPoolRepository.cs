using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmCustomerPoolRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmCustomerPoolEntry>(clientResolver), ICrmCustomerPoolRepository
{
    public async Task<SysCrmCustomerPoolEntry?> GetByCustomerAsync(
        long customerId,
        CancellationToken cancellationToken = default)
        => (await CreateQueryable().Where(item => item.CustomerId == customerId)
            .Take(1).ToListAsync(cancellationToken)).FirstOrDefault();

    public async Task<IReadOnlyList<SysCrmCustomerPoolEntry>> GetAvailableAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        return await CreateQueryable()
            .Where(item => item.Status == CrmCustomerPoolStatus.Available)
            .OrderBy(item => item.CreatedTime)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SysCrmCustomerPoolEntry>> GetIdleClaimsAsync(
        DateTimeOffset thresholdUtc,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        return await CreateQueryable()
            .Where(item => item.Status == CrmCustomerPoolStatus.Claimed
                && item.ClaimedAtUtc != null
                && item.ClaimedAtUtc < thresholdUtc)
            .OrderBy(item => item.ClaimedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Atomic conditional update: only one caller can flip Available to Claimed.</summary>
    public Task<bool> TryClaimAsync(
        long entryId,
        long userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
        => UpdateAsync(
            entry => new SysCrmCustomerPoolEntry
            {
                Status = CrmCustomerPoolStatus.Claimed,
                ClaimedUserId = userId,
                ClaimedAtUtc = nowUtc,
                ClaimCount = entry.ClaimCount + 1
            },
            entry => entry.BasicId == entryId && entry.Status == CrmCustomerPoolStatus.Available,
            cancellationToken);

    /// <summary>Atomic release: the claimant check is part of the WHERE clause.</summary>
    public Task<bool> TryReleaseAsync(
        long entryId,
        long userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
        => UpdateAsync(
            entry => new SysCrmCustomerPoolEntry
            {
                Status = CrmCustomerPoolStatus.Available,
                ClaimedUserId = null,
                ClaimedAtUtc = null,
                LastReleasedAtUtc = nowUtc
            },
            entry => entry.BasicId == entryId
                && entry.Status == CrmCustomerPoolStatus.Claimed
                && entry.ClaimedUserId == userId,
            cancellationToken);

    /// <summary>Atomic recovery of an idle claim back to the pool.</summary>
    public Task<bool> TryRecoverAsync(
        long entryId,
        DateTimeOffset thresholdUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
        => UpdateAsync(
            entry => new SysCrmCustomerPoolEntry
            {
                Status = CrmCustomerPoolStatus.Recovered,
                ClaimedUserId = null,
                LastReleasedAtUtc = nowUtc
            },
            entry => entry.BasicId == entryId
                && entry.Status == CrmCustomerPoolStatus.Claimed
                && entry.ClaimedAtUtc != null
                && entry.ClaimedAtUtc < thresholdUtc,
            cancellationToken);
}
