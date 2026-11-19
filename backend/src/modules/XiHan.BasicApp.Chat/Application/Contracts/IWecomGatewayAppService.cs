using XiHan.BasicApp.Chat.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Chat.Application.Contracts;

public interface IWecomGatewayAppService : IApplicationService
{
    Task<WecomBindingDto> UpsertBindingAsync(WecomBindingInputDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WecomBindingDto>> GetBindingsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WecomCommandAuditDto>> GetAuditAsync(CancellationToken cancellationToken = default);
}
