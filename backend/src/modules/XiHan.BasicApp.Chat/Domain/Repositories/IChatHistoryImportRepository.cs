using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Chat.Domain.Repositories;

public interface IChatHistoryImportBatchRepository : ISaasRepository<SysChatHistoryImportBatch>
{
    Task<IReadOnlyList<SysChatHistoryImportBatch>> GetRecentAsync(int take, CancellationToken cancellationToken = default);
}

public interface IChatHistoryImportRowRepository : ISaasRepository<SysChatHistoryImportRow>
{
    Task<IReadOnlyList<SysChatHistoryImportRow>> GetBySourceChannelAsync(string sourceChannel, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysChatHistoryImportRow>> GetByBatchAsync(long batchId, CancellationToken cancellationToken = default);
}
