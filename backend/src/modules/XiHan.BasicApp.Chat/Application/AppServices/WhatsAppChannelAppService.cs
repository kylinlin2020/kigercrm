using XiHan.BasicApp.Chat.Application;
using XiHan.BasicApp.Chat.Application.Contracts;
using XiHan.BasicApp.Chat.Application.Dtos;
using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.BasicApp.Chat.Domain.Permissions;
using XiHan.BasicApp.Chat.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Chat.Application.AppServices;

/// <summary>
/// Tenant-owned WhatsApp account administration and webhook inbox review. This service never
/// sends a message and never accepts a plaintext token: the credential reference is a key into
/// the tenant secret store.
/// </summary>
[DynamicApi(Group = "BasicApp.Chat", GroupName = "在线聊天", Tag = "WhatsApp")]
public sealed class WhatsAppChannelAppService(
    IWhatsAppChannelAccountRepository accounts,
    ISqlSugarClientResolver clientResolver)
    : ChatApplicationService, IWhatsAppChannelAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(ChatPermissionCodes.Manage)]
    public async Task<WhatsAppChannelAccountDto> UpsertBindingAsync(
        WhatsAppChannelBindingDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.WhatsAppBusinessAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.PhoneNumberId);
        if (!IsSafeRouteId(input.WhatsAppBusinessAccountId) || !IsSafeRouteId(input.PhoneNumberId))
            throw new ArgumentException("WhatsApp route ids may only contain letters, digits, '-' and '_'.", nameof(input));
        if (input.CredentialRef is { Length: > 300 } || input.CredentialRef?.Contains("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            throw new ArgumentException("CredentialRef must be a secret-store reference, never a token.", nameof(input));

        var existing = (await accounts.GetListAsync(item =>
                item.WhatsAppBusinessAccountId == input.WhatsAppBusinessAccountId
                && item.PhoneNumberId == input.PhoneNumberId, cancellationToken))
            .FirstOrDefault();
        var entity = existing ?? new SysWhatsAppChannelAccount
        {
            WhatsAppBusinessAccountId = input.WhatsAppBusinessAccountId.Trim(),
            PhoneNumberId = input.PhoneNumberId.Trim()
        };
        entity.DisplayName = input.DisplayName.Trim();
        entity.CredentialRef = input.CredentialRef?.Trim();
        entity.Status = input.Active ? WhatsAppChannelAccountStatus.Active : WhatsAppChannelAccountStatus.Disabled;
        var saved = existing is null
            ? await accounts.AddAsync(entity, cancellationToken)
            : await accounts.UpdateAsync(entity, cancellationToken);
        return ToDto(saved);
    }

    [PermissionAuthorize(ChatPermissionCodes.Read)]
    public async Task<IReadOnlyList<WhatsAppChannelAccountDto>> GetBindingsAsync(CancellationToken cancellationToken = default)
        => (await accounts.GetAllAsync(cancellationToken)).Select(ToDto).ToArray();

    [PermissionAuthorize(ChatPermissionCodes.Read)]
    public async Task<IReadOnlyList<WhatsAppWebhookInboxDto>> GetInboxAsync(int take, CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        var db = clientResolver.GetClientForEntity(typeof(SysWhatsAppWebhookInbox));
        var rows = await db.Queryable<SysWhatsAppWebhookInbox>()
            .OrderByDescending(item => item.CreatedTime)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(item => new WhatsAppWebhookInboxDto
        {
            BasicId = item.BasicId,
            WhatsAppBusinessAccountId = item.WhatsAppBusinessAccountId,
            PhoneNumberId = item.PhoneNumberId,
            EventType = item.EventType,
            ProviderMessageId = item.ProviderMessageId,
            SenderWhatsAppId = item.SenderWhatsAppId,
            RecipientWhatsAppId = item.RecipientWhatsAppId,
            ProviderTimestamp = item.ProviderTimestamp,
            Status = item.Status,
            AttemptCount = item.AttemptCount,
            ProcessedTime = item.ProcessedTime,
            LastError = item.LastError
        }).ToArray();
    }

    private static bool IsSafeRouteId(string value)
        => value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    private static WhatsAppChannelAccountDto ToDto(SysWhatsAppChannelAccount entity) => new()
    {
        BasicId = entity.BasicId,
        DisplayName = entity.DisplayName,
        WhatsAppBusinessAccountId = entity.WhatsAppBusinessAccountId,
        PhoneNumberId = entity.PhoneNumberId,
        CredentialRef = entity.CredentialRef,
        Status = (int)entity.Status,
        CreatedTime = entity.CreatedTime
    };
}
