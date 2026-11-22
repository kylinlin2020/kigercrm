using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Security.Users;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>
/// Customer shared pool. Claim/release/assign/recover are exclusive conditional updates; the
/// policy guards claim limits and idle recovery. No automatic outreach or external call occurs.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "CustomerPool")]
public sealed class CrmCustomerPoolAppService(
    ICrmCustomerPoolRepository entries,
    ICrmCustomerRepository customers,
    ICurrentUser currentUser)
    : CrmApplicationService, ICrmCustomerPoolAppService
{
    private const int MaxListSize = 100;

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmCustomerPoolEntryDto> AddAsync(
        CrmCustomerPoolAddDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var customer = await customers.GetByIdAsync(input.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer does not exist.");
        var existing = await entries.GetByCustomerAsync(input.CustomerId, cancellationToken);
        if (existing is not null) return await ToDto(existing, cancellationToken);
        var created = await entries.AddAsync(new SysCrmCustomerPoolEntry
        {
            CustomerId = customer.BasicId,
            Status = CrmCustomerPoolStatus.Available,
            ClaimCount = 0,
            Note = CrmCustomerPoolPolicy.NormalizeNote(input.Note)
        }, cancellationToken);
        return await ToDto(created, cancellationToken);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmCustomerPoolEntryDto>> GetAvailableAsync(CancellationToken cancellationToken = default)
    {
        var rows = await entries.GetAvailableAsync(MaxListSize, cancellationToken);
        var result = new List<CrmCustomerPoolEntryDto>(rows.Count);
        foreach (var row in rows) result.Add(await ToDto(row, cancellationToken));
        return result;
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmCustomerPoolEntryDto>> GetMineAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var rows = await entries.GetAllAsync(cancellationToken);
        var result = new List<CrmCustomerPoolEntryDto>();
        foreach (var row in rows.Where(item => item.ClaimedUserId == userId).Take(MaxListSize))
            result.Add(await ToDto(row, cancellationToken));
        return result;
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmCustomerPoolEntryDto> ClaimAsync(
        CrmCustomerPoolActionDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var userId = RequireUserId();
        var entry = await GetEntryAsync(input.EntryId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var gate = CrmCustomerPoolPolicy.CanClaim(entry, now);
        if (!gate.Allowed) throw new InvalidOperationException($"Cannot claim customer: {gate.Reason}.");
        if (!await entries.TryClaimAsync(entry.BasicId, userId, now, cancellationToken))
            throw new InvalidOperationException("Customer was claimed by another user; please retry.");
        return await ToDto(await GetEntryAsync(entry.BasicId, cancellationToken), cancellationToken);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmCustomerPoolEntryDto> ReleaseAsync(
        CrmCustomerPoolActionDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entry = await GetEntryAsync(input.EntryId, cancellationToken);
        var gate = CrmCustomerPoolPolicy.CanRelease(entry, RequireUserId());
        if (!gate.Allowed) throw new InvalidOperationException($"Cannot release customer: {gate.Reason}.");
        if (!await entries.TryReleaseAsync(entry.BasicId, entry.ClaimedUserId!.Value, DateTimeOffset.UtcNow, cancellationToken))
            throw new InvalidOperationException("Release failed; please retry.");
        return await ToDto(await GetEntryAsync(entry.BasicId, cancellationToken), cancellationToken);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmCustomerPoolEntryDto> AssignAsync(
        long entryId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        var entry = await GetEntryAsync(entryId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var gate = CrmCustomerPoolPolicy.CanClaim(entry, now);
        if (!gate.Allowed) throw new InvalidOperationException($"Cannot assign customer: {gate.Reason}.");
        if (!await entries.TryClaimAsync(entry.BasicId, userId, now, cancellationToken))
            throw new InvalidOperationException("Assignment failed; entry is no longer available.");
        return await ToDto(await GetEntryAsync(entry.BasicId, cancellationToken), cancellationToken);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<int> RecoverIdleAsync(CancellationToken cancellationToken = default)
        => await CrmCustomerPoolReclamation.RecoverIdleAsync(entries, cancellationToken);

    private long RequireUserId()
        => currentUser.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    private async Task<SysCrmCustomerPoolEntry> GetEntryAsync(long id, CancellationToken cancellationToken)
        => await entries.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Customer pool entry does not exist.");

    private async Task<CrmCustomerPoolEntryDto> ToDto(
        SysCrmCustomerPoolEntry entry,
        CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(entry.CustomerId, cancellationToken);
        return new CrmCustomerPoolEntryDto
        {
            BasicId = entry.BasicId,
            CustomerId = entry.CustomerId,
            CustomerName = customer?.Name ?? string.Empty,
            Status = (int)entry.Status,
            ClaimedUserId = entry.ClaimedUserId,
            ClaimedAtUtc = entry.ClaimedAtUtc,
            ClaimCount = entry.ClaimCount,
            LastReleasedAtUtc = entry.LastReleasedAtUtc
        };
    }
}
