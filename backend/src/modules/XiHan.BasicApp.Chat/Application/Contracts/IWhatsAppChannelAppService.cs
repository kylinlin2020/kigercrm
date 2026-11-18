using XiHan.BasicApp.Chat.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Chat.Application.Contracts;

public interface IWhatsAppChannelAppService : IApplicationService
{
    Task<WhatsAppChannelAccountDto> UpsertBindingAsync(WhatsAppChannelBindingDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WhatsAppChannelAccountDto>> GetBindingsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WhatsAppWebhookInboxDto>> GetInboxAsync(int take, CancellationToken cancellationToken = default);
}
