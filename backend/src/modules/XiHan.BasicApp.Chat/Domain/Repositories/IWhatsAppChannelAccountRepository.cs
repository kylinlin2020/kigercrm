using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Chat.Domain.Repositories;

public interface IWhatsAppChannelAccountRepository : ISaasRepository<SysWhatsAppChannelAccount>
{
    Task<SysWhatsAppChannelAccount?> FindByRouteAsync(
        string whatsappBusinessAccountId,
        string phoneNumberId,
        CancellationToken cancellationToken = default);

    /// <summary>Platform-level routing lookup used by the anonymous webhook before a tenant scope exists.</summary>
    Task<SysWhatsAppChannelAccount?> FindByRouteAnyTenantAsync(
        string whatsappBusinessAccountId,
        string phoneNumberId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysWhatsAppChannelAccount>> GetAllAsync(CancellationToken cancellationToken = default);
}
