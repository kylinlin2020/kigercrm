using XiHan.BasicApp.Chat.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Chat.Application.Contracts;

public interface IBaileysSessionAppService : IApplicationService
{
    Task<BaileysSessionDto> CreateSessionAsync(BaileysSessionCreateDto input, CancellationToken cancellationToken = default);
    Task<BaileysSessionDto> TransitionAsync(BaileysSessionTransitionDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BaileysSessionDto>> GetSessionsAsync(CancellationToken cancellationToken = default);
}
