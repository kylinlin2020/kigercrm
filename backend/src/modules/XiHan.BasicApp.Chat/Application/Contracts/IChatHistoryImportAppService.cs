using XiHan.BasicApp.Chat.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Chat.Application.Contracts;

public interface IChatHistoryImportAppService : IApplicationService
{
    Task<ChatHistoryImportResultDto> PreviewAsync(ChatHistoryImportInputDto input, CancellationToken cancellationToken = default);
    Task<ChatHistoryImportResultDto> ImportAsync(ChatHistoryImportInputDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatHistoryImportBatchDto>> GetBatchesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatHistoryImportVerdictDto>> GetRowsAsync(long batchId, CancellationToken cancellationToken = default);
}
