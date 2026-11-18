using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.BasicApp.Chat.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Chat.Infrastructure.Repositories;

public sealed class WhatsAppChannelAccountRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysWhatsAppChannelAccount>(clientResolver), IWhatsAppChannelAccountRepository
{
    public async Task<SysWhatsAppChannelAccount?> FindByRouteAsync(
        string whatsappBusinessAccountId,
        string phoneNumberId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(whatsappBusinessAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumberId);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await CreateQueryable().Where(item =>
                item.WhatsAppBusinessAccountId == whatsappBusinessAccountId
                && item.PhoneNumberId == phoneNumberId
                && item.Status == WhatsAppChannelAccountStatus.Active)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<SysWhatsAppChannelAccount?> FindByRouteAnyTenantAsync(
        string whatsappBusinessAccountId,
        string phoneNumberId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(whatsappBusinessAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumberId);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await CreateQueryable().ClearFilter().Where(item =>
                item.WhatsAppBusinessAccountId == whatsappBusinessAccountId
                && item.PhoneNumberId == phoneNumberId
                && item.Status == WhatsAppChannelAccountStatus.Active)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SysWhatsAppChannelAccount>> GetAllAsync(CancellationToken cancellationToken = default)
        => await CreateQueryable().OrderByDescending(item => item.CreatedTime).Take(100).ToListAsync(cancellationToken);
}
