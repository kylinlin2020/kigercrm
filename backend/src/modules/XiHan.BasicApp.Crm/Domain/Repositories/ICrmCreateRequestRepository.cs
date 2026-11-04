using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped access to the permanent CRM document-create request ledger.</summary>
public interface ICrmCreateRequestRepository : ISaasRepository<SysCrmCreateRequest>
{
    Task<SysCrmCreateRequest?> GetByRequestKeyAsync(
        string documentType,
        string requestKey,
        CancellationToken cancellationToken = default);
}
