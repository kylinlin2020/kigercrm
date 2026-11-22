using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

public interface ICrmCustomerPoolRepository : ISaasRepository<SysCrmCustomerPoolEntry>
{
    Task<SysCrmCustomerPoolEntry?> GetByCustomerAsync(long customerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysCrmCustomerPoolEntry>> GetAvailableAsync(int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysCrmCustomerPoolEntry>> GetIdleClaimsAsync(DateTimeOffset thresholdUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>Atomically claim an available entry; only one caller can win the same entry.</summary>
    Task<bool> TryClaimAsync(long entryId, long userId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Atomically release a claim back to the pool, keeping the claimant check in SQL.</summary>
    Task<bool> TryReleaseAsync(long entryId, long userId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Atomically recover an idle claim back to the pool.</summary>
    Task<bool> TryRecoverAsync(long entryId, DateTimeOffset thresholdUtc, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}
