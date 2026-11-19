using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Chat.Infrastructure.Repositories;

public sealed class ChatHistoryImportBatchRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysChatHistoryImportBatch>(clientResolver), IChatHistoryImportBatchRepository
{
    public async Task<IReadOnlyList<SysChatHistoryImportBatch>> GetRecentAsync(int take, CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(take));
        return await CreateQueryable().OrderByDescending(item => item.CreatedTime).Take(take).ToListAsync(cancellationToken);
    }
}

public sealed class ChatHistoryImportRowRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysChatHistoryImportRow>(clientResolver), IChatHistoryImportRowRepository
{
    public async Task<IReadOnlyList<SysChatHistoryImportRow>> GetBySourceChannelAsync(
        string sourceChannel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceChannel);
        cancellationToken.ThrowIfCancellationRequested();
        return await CreateQueryable().Where(item => item.SourceChannel == sourceChannel)
            .Take(10_000)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SysChatHistoryImportRow>> GetByBatchAsync(long batchId, CancellationToken cancellationToken = default)
    {
        if (batchId <= 0) throw new ArgumentOutOfRangeException(nameof(batchId));
        return await CreateQueryable().Where(item => item.BatchId == batchId)
            .OrderBy(item => item.CreatedTime).Take(500).ToListAsync(cancellationToken);
    }
}
