using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmTradeDocumentImportRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmTradeDocumentImportRow>(clientResolver), ICrmTradeDocumentImportRepository
{
    public async Task<IReadOnlyList<SysCrmTradeDocumentImportRow>> GetByBatchAsync(
        string batchId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        return await CreateQueryable().Where(row => row.BatchId == batchId)
            .OrderBy(row => row.CreatedTime)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetBatchIdsAsync(CancellationToken cancellationToken = default)
        => await CreateQueryable().Select(row => row.BatchId).Distinct().Take(100).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SysCrmTradeDocumentImportRow>> GetByDocumentKeysAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0) return [];
        return await CreateQueryable().Where(row => keys.Contains(row.NormalizedDocumentKey))
            .Take(500)
            .ToListAsync(cancellationToken);
    }
}
