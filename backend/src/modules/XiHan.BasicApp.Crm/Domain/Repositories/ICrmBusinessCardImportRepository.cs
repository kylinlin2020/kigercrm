using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

public interface ICrmBusinessCardImportRepository : ISaasRepository<SysCrmBusinessCardImportRow>
{
    Task<IReadOnlyList<SysCrmBusinessCardImportRow>> GetByBatchAsync(string batchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetBatchIdsAsync(CancellationToken cancellationToken = default);
}
