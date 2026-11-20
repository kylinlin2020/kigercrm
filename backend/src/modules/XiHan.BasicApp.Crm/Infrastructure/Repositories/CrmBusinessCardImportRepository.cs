using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmBusinessCardImportRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmBusinessCardImportRow>(clientResolver), ICrmBusinessCardImportRepository
{
    public async Task<IReadOnlyList<SysCrmBusinessCardImportRow>> GetByBatchAsync(
        string batchId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        return await CreateQueryable().Where(row => row.BatchId == batchId)
            .OrderBy(row => row.CreatedTime)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetBatchIdsAsync(CancellationToken cancellationToken = default)
        => await CreateQueryable()
            .Select(row => row.BatchId)
            .Distinct()
            .Take(100)
            .ToListAsync(cancellationToken);
}
