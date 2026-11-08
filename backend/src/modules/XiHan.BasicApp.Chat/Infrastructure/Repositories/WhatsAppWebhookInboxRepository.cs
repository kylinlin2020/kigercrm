// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using SqlSugar;
using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Chat.Infrastructure.Repositories;

/// <summary>
/// PostgreSQL-backed WhatsApp inbox lease store. Every operation is tenant-scoped and
/// uses database time plus monotonically increasing fencing to reject stale consumers.
/// </summary>
public sealed class WhatsAppWebhookInboxRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysWhatsAppWebhookInbox>(clientResolver), IWhatsAppWebhookInboxRepository
{
    private const int MaxSafeErrorLength = 1000;

    public async Task<IReadOnlyList<SysWhatsAppWebhookInbox>> ClaimDueAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (leaseOwner.Length > 160) throw new ArgumentOutOfRangeException(nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        cancellationToken.ThrowIfCancellationRequested();

        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("WhatsApp inbox claims require PostgreSQL.");
        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for WhatsApp inbox claims.");
        var durationMilliseconds = checked((long)Math.Ceiling(leaseDuration.TotalMilliseconds));

        const string sql = """
            WITH due_events AS (
                SELECT inbox.basic_id
                FROM sys_whatsapp_webhook_inbox AS inbox
                WHERE inbox.tenant_id = @tenantId
                  AND inbox.status IN (@pendingStatus, @retryableStatus)
                  AND (inbox.next_attempt_time IS NULL OR inbox.next_attempt_time <= clock_timestamp())
                  AND (inbox.lease_expires_at IS NULL OR inbox.lease_expires_at <= clock_timestamp())
                ORDER BY COALESCE(inbox.next_attempt_time, inbox.created_time), inbox.basic_id
                LIMIT @batchSize
                FOR UPDATE SKIP LOCKED
            )
            UPDATE sys_whatsapp_webhook_inbox AS inbox
            SET lease_owner = @leaseOwner,
                lease_token = gen_random_uuid(),
                lease_expires_at = clock_timestamp() + (@leaseDurationMilliseconds * INTERVAL '1 millisecond'),
                fencing_version = inbox.fencing_version + 1,
                attempt_count = inbox.attempt_count + 1
            FROM due_events
            WHERE inbox.tenant_id = @tenantId
              AND inbox.basic_id = due_events.basic_id
            RETURNING inbox.*;
            """;

        return await client.Ado.SqlQueryAsync<SysWhatsAppWebhookInbox>(
            sql,
            new SugarParameter("@tenantId", tenant.TenantId),
            new SugarParameter("@pendingStatus", WhatsAppWebhookInboxStatus.Pending),
            new SugarParameter("@retryableStatus", WhatsAppWebhookInboxStatus.Retryable),
            new SugarParameter("@leaseOwner", leaseOwner),
            new SugarParameter("@leaseDurationMilliseconds", durationMilliseconds),
            new SugarParameter("@batchSize", batchSize));
    }

    public async Task<bool> TryRenewLeaseAsync(
        long inboxId,
        Guid leaseToken,
        long fencingVersion,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ValidateLease(inboxId, leaseToken, fencingVersion);
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for WhatsApp inbox lease renewal.");
        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("WhatsApp inbox lease renewal requires PostgreSQL.");

        const string sql = """
            UPDATE sys_whatsapp_webhook_inbox
            SET lease_expires_at = clock_timestamp() + (@leaseDurationMilliseconds * INTERVAL '1 millisecond')
            WHERE tenant_id = @tenantId AND basic_id = @inboxId
              AND status IN (@pendingStatus, @retryableStatus)
              AND lease_token = @leaseToken
              AND fencing_version = @fencingVersion
              AND lease_expires_at > clock_timestamp();
            """;
        var affected = await client.Ado.ExecuteCommandAsync(sql,
            new SugarParameter("@leaseDurationMilliseconds", checked((long)Math.Ceiling(leaseDuration.TotalMilliseconds))),
            new SugarParameter("@tenantId", tenant.TenantId),
            new SugarParameter("@inboxId", inboxId),
            new SugarParameter("@pendingStatus", WhatsAppWebhookInboxStatus.Pending),
            new SugarParameter("@retryableStatus", WhatsAppWebhookInboxStatus.Retryable),
            new SugarParameter("@leaseToken", leaseToken),
            new SugarParameter("@fencingVersion", fencingVersion));
        return affected == 1;
    }

    public async Task<bool> TryMarkProcessedAsync(
        long inboxId,
        Guid leaseToken,
        long fencingVersion,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateLease(inboxId, leaseToken, fencingVersion);
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for WhatsApp inbox completion.");
        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("WhatsApp inbox completion requires PostgreSQL.");

        const string sql = """
            UPDATE sys_whatsapp_webhook_inbox
            SET status = @processedStatus,
                processed_time = @processedAtUtc,
                last_error = NULL,
                lease_owner = NULL,
                lease_token = NULL,
                lease_expires_at = NULL
            WHERE tenant_id = @tenantId AND basic_id = @inboxId
              AND status IN (@pendingStatus, @retryableStatus)
              AND lease_token = @leaseToken
              AND fencing_version = @fencingVersion
              AND lease_expires_at > clock_timestamp();
            """;
        var affected = await client.Ado.ExecuteCommandAsync(sql,
            new SugarParameter("@processedStatus", WhatsAppWebhookInboxStatus.Processed),
            new SugarParameter("@processedAtUtc", processedAtUtc),
            new SugarParameter("@tenantId", tenant.TenantId),
            new SugarParameter("@inboxId", inboxId),
            new SugarParameter("@pendingStatus", WhatsAppWebhookInboxStatus.Pending),
            new SugarParameter("@retryableStatus", WhatsAppWebhookInboxStatus.Retryable),
            new SugarParameter("@leaseToken", leaseToken),
            new SugarParameter("@fencingVersion", fencingVersion));
        return affected == 1;
    }

    public async Task<bool> TryScheduleRetryAsync(
        long inboxId,
        Guid leaseToken,
        long fencingVersion,
        DateTimeOffset nextAttemptAtUtc,
        string safeError,
        bool needsReview = false,
        CancellationToken cancellationToken = default)
    {
        ValidateLease(inboxId, leaseToken, fencingVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeError);
        safeError = safeError.Trim();
        if (safeError.Length > MaxSafeErrorLength)
            safeError = safeError[..MaxSafeErrorLength];
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for WhatsApp inbox retry.");
        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("WhatsApp inbox retry requires PostgreSQL.");

        const string sql = """
            UPDATE sys_whatsapp_webhook_inbox
            SET status = @nextStatus,
                next_attempt_time = @nextAttemptAtUtc,
                last_error = @safeError,
                lease_owner = NULL,
                lease_token = NULL,
                lease_expires_at = NULL
            WHERE tenant_id = @tenantId AND basic_id = @inboxId
              AND status IN (@pendingStatus, @retryableStatus)
              AND lease_token = @leaseToken
              AND fencing_version = @fencingVersion
              AND lease_expires_at > clock_timestamp();
            """;
        var affected = await client.Ado.ExecuteCommandAsync(sql,
            new SugarParameter("@nextStatus", needsReview ? WhatsAppWebhookInboxStatus.NeedsReview : WhatsAppWebhookInboxStatus.Retryable),
            new SugarParameter("@nextAttemptAtUtc", nextAttemptAtUtc),
            new SugarParameter("@safeError", safeError),
            new SugarParameter("@tenantId", tenant.TenantId),
            new SugarParameter("@inboxId", inboxId),
            new SugarParameter("@pendingStatus", WhatsAppWebhookInboxStatus.Pending),
            new SugarParameter("@retryableStatus", WhatsAppWebhookInboxStatus.Retryable),
            new SugarParameter("@leaseToken", leaseToken),
            new SugarParameter("@fencingVersion", fencingVersion));
        return affected == 1;
    }

    private static void ValidateLease(long inboxId, Guid leaseToken, long fencingVersion)
    {
        if (inboxId <= 0) throw new ArgumentOutOfRangeException(nameof(inboxId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
    }
}
