using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmCustomerRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmCustomer>(clientResolver), ICrmCustomerRepository
{
    public async Task<IReadOnlyList<SysCrmCustomer>> GetByNameKeysAsync(
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedNames);
        if (normalizedNames.Count == 0) return [];
        var matches = new List<SysCrmCustomer>();
        foreach (var batch in normalizedNames.Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase).Chunk(300))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keys = batch.ToArray();
            matches.AddRange(await CreateQueryable()
                .Where(customer => keys.Contains(customer.Name.ToLower()))
                .Take(300)
                .ToListAsync(cancellationToken));
        }
        return matches;
    }
}