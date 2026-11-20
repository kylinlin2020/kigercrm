using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

public interface ICrmTradeDocumentImportRepository : ISaasRepository<SysCrmTradeDocumentImportRow>
{
    Task<IReadOnlyList<SysCrmTradeDocumentImportRow>> GetByBatchAsync(string batchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetBatchIdsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysCrmTradeDocumentImportRow>> GetByDocumentKeysAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default);
}
