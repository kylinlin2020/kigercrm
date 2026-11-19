using XiHan.BasicApp.Chat.Application;
using XiHan.BasicApp.Chat.Application.Contracts;
using XiHan.BasicApp.Chat.Application.Dtos;
using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.BasicApp.Chat.Domain.Permissions;
using XiHan.BasicApp.Chat.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Chat.Application.AppServices;

/// <summary>WeCom binding administration and command-audit review; no outbound send is exposed.</summary>
[DynamicApi(Group = "BasicApp.Chat", GroupName = "在线聊天", Tag = "WeCom")]
public sealed class WecomGatewayAppService(
    IWecomBindingRepository bindings,
    IWecomCommandAuditRepository audits)
    : ChatApplicationService, IWecomGatewayAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(ChatPermissionCodes.Manage)]
    public async Task<WecomBindingDto> UpsertBindingAsync(WecomBindingInputDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CorpId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.AgentId);
        if (!IsSafeId(input.CorpId) || !IsSafeId(input.AgentId))
            throw new ArgumentException("WeCom route ids may only contain letters, digits, '-' and '_'.", nameof(input));
        if (input.CredentialRef is { Length: > 300 })
            throw new ArgumentOutOfRangeException(nameof(input.CredentialRef));

        var existing = (await bindings.GetAllAsync(cancellationToken)).FirstOrDefault(item =>
            item.CorpId == input.CorpId && item.AgentId == input.AgentId);
        var entity = existing ?? new SysWecomAccountBinding
        {
            CorpId = input.CorpId.Trim(),
            AgentId = input.AgentId.Trim()
        };
        entity.DisplayName = input.DisplayName.Trim();
        entity.CredentialRef = input.CredentialRef?.Trim();
        entity.Status = input.Active ? WecomBindingStatus.Active : WecomBindingStatus.Disabled;
        var saved = existing is null
            ? await bindings.AddAsync(entity, cancellationToken)
            : await bindings.UpdateAsync(entity, cancellationToken);
        return ToDto(saved);
    }

    [PermissionAuthorize(ChatPermissionCodes.Read)]
    public async Task<IReadOnlyList<WecomBindingDto>> GetBindingsAsync(CancellationToken cancellationToken = default)
        => (await bindings.GetAllAsync(cancellationToken)).Select(ToDto).ToArray();

    [PermissionAuthorize(ChatPermissionCodes.Read)]
    public async Task<IReadOnlyList<WecomCommandAuditDto>> GetAuditAsync(CancellationToken cancellationToken = default)
        => (await audits.GetRecentAsync(100, cancellationToken)).Select(item => new WecomCommandAuditDto
        {
            BasicId = item.BasicId,
            CorpId = item.CorpId,
            AgentId = item.AgentId,
            FromUserId = item.FromUserId,
            ProviderMessageId = item.ProviderMessageId,
            Command = item.Command,
            Outcome = (int)item.Outcome,
            Reason = item.Reason,
            ReplyHash = item.ReplyHash
        }).ToArray();

    private static bool IsSafeId(string value) => value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    private static WecomBindingDto ToDto(SysWecomAccountBinding entity) => new()
    {
        BasicId = entity.BasicId,
        DisplayName = entity.DisplayName,
        CorpId = entity.CorpId,
        AgentId = entity.AgentId,
        CredentialRef = entity.CredentialRef,
        Status = (int)entity.Status,
        CreatedTime = entity.CreatedTime
    };
}
