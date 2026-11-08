using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>
/// Bounded read-only customer lookup. SaasRepository supplies the current tenant's
/// query filters; IDs are queried in chunks to keep SQL parameter lists bounded.
/// </summary>
public sealed class CrmCustomerLookupRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmCustomer>(clientResolver), ICrmCustomerLookupRepository
{
    private const int MaxIdsPerQuery = 500;
    private const int MaxIdsPerRequest = 5_000;

    public async Task<IReadOnlySet<long>> GetExistingIdsAsync(
        IReadOnlyCollection<long> customerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customerIds);
        cancellationToken.ThrowIfCancellationRequested();

        var ids = customerIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length > MaxIdsPerRequest)
            throw new ArgumentOutOfRangeException(nameof(customerIds), $"At most {MaxIdsPerRequest} customer IDs can be checked per preview.");

        var existing = new HashSet<long>();
        for (var offset = 0; offset < ids.Length; offset += MaxIdsPerQuery)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = ids.Skip(offset).Take(MaxIdsPerQuery).ToArray();
            var found = await CreateQueryable()
                .Where(customer => batch.Contains(customer.BasicId))
                .Select(customer => customer.BasicId)
                .ToListAsync(cancellationToken);

            existing.UnionWith(found);
        }

        return existing;
    }
}
