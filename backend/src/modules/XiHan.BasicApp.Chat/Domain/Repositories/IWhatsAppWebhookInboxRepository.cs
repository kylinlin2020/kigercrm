// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Chat.Domain.Repositories;

/// <summary>Tenant-scoped durable inbox operations for verified Meta WhatsApp events.</summary>
public interface IWhatsAppWebhookInboxRepository : ISaasRepository<SysWhatsAppWebhookInbox>
{
    /// <summary>Atomically claims due events with PostgreSQL SKIP LOCKED and a new fencing token.</summary>
    Task<IReadOnlyList<SysWhatsAppWebhookInbox>> ClaimDueAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>Renews only the exact still-live lease; an expired lease cannot be resurrected.</summary>
    Task<bool> TryRenewLeaseAsync(
        long inboxId,
        Guid leaseToken,
        long fencingVersion,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>Completes an event only while the exact unexpired lease still owns it.</summary>
    Task<bool> TryMarkProcessedAsync(
        long inboxId,
        Guid leaseToken,
        long fencingVersion,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Schedules a retry or quarantines an event, guarded by the exact live lease.</summary>
    Task<bool> TryScheduleRetryAsync(
        long inboxId,
        Guid leaseToken,
        long fencingVersion,
        DateTimeOffset nextAttemptAtUtc,
        string safeError,
        bool needsReview = false,
        CancellationToken cancellationToken = default);
}
