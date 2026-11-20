using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped customer repository with bounded name-key lookup for import de-duplication.</summary>
public interface ICrmCustomerRepository : ISaasRepository<SysCrmCustomer>
{
    Task<IReadOnlyList<SysCrmCustomer>> GetByNameKeysAsync(
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken = default);
}