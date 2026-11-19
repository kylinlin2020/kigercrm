using XiHan.BasicApp.Chat.Application;
using XiHan.BasicApp.Chat.Application.Contracts;
using XiHan.BasicApp.Chat.Application.Dtos;
using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.BasicApp.Chat.Domain.Permissions;
using XiHan.BasicApp.Chat.Domain.Prospecting;
using XiHan.BasicApp.Chat.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Chat.Application.AppServices;

/// <summary>
/// Baileys session contract administration. Connection state is explicitly confirmed by an
/// operator; no QR payload, auth state or device operation is exposed or executed here.
/// </summary>
[DynamicApi(Group = "BasicApp.Chat", GroupName = "在线聊天", Tag = "Baileys")]
public sealed class BaileysSessionAppService(
    IBaileysSessionRepository sessions)
    : ChatApplicationService, IBaileysSessionAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(ChatPermissionCodes.Manage)]
    public async Task<BaileysSessionDto> CreateSessionAsync(
        BaileysSessionCreateDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DisplayName);
        if (input.AuthStateRef is { Length: > 300 })
            throw new ArgumentOutOfRangeException(nameof(input.AuthStateRef));
        var entity = await sessions.AddAsync(new SysBaileysSession
        {
            DisplayName = input.DisplayName.Trim(),
            AuthStateRef = input.AuthStateRef?.Trim(),
            Status = BaileysSessionStatus.Disconnected,
            Health = BaileysHealthState.Unknown
        }, cancellationToken);
        return ToDto(entity);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(ChatPermissionCodes.Manage)]
    public async Task<BaileysSessionDto> TransitionAsync(
        BaileysSessionTransitionDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(typeof(BaileysSessionStatus), input.TargetStatus))
            throw new ArgumentOutOfRangeException(nameof(input.TargetStatus));
        var entity = await sessions.GetByIdAsync(input.SessionId, cancellationToken)
            ?? throw new InvalidOperationException("Baileys session does not exist.");
        var now = DateTimeOffset.UtcNow;
        var target = (BaileysSessionStatus)input.TargetStatus;
        var decision = BaileysSessionPolicy.CanTransition(entity.Status, target, now, entity.QrIssuedAtUtc);
        if (!decision.Allowed)
            throw new InvalidOperationException($"Baileys transition rejected: {decision.Reason}.");

        entity.Status = target;
        entity.Note = decision.Reason;
        if (target == BaileysSessionStatus.QrPending) entity.QrIssuedAtUtc = now;
        if (target == BaileysSessionStatus.Connected)
        {
            entity.LastConnectedAtUtc = now;
            entity.Health = BaileysHealthState.Healthy;
        }
        if (target == BaileysSessionStatus.Disconnected)
            entity.Health = BaileysSessionPolicy.ClassifyHealth(entity.LastConnectedAtUtc, now);
        if (target == BaileysSessionStatus.Revoked)
        {
            entity.AuthStateRef = null;
            entity.QrIssuedAtUtc = null;
            entity.Health = BaileysHealthState.Offline;
        }
        return ToDto(await sessions.UpdateAsync(entity, cancellationToken));
    }

    [PermissionAuthorize(ChatPermissionCodes.Read)]
    public async Task<IReadOnlyList<BaileysSessionDto>> GetSessionsAsync(CancellationToken cancellationToken = default)
        => (await sessions.GetAllAsync(cancellationToken)).Select(ToDto).ToArray();

    private static BaileysSessionDto ToDto(SysBaileysSession entity) => new()
    {
        BasicId = entity.BasicId,
        DisplayName = entity.DisplayName,
        AuthStateRef = entity.AuthStateRef,
        Status = (int)entity.Status,
        Health = (int)entity.Health,
        QrIssuedAtUtc = entity.QrIssuedAtUtc,
        LastConnectedAtUtc = entity.LastConnectedAtUtc,
        Note = entity.Note,
        CreatedTime = entity.CreatedTime
    };
}
