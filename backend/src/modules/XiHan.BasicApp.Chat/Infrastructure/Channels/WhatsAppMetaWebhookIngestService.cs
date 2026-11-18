using Microsoft.Extensions.Logging;
using SqlSugar;
using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Chat.Infrastructure.Channels;

public sealed record WhatsAppWebhookIngestResult(int Accepted, int Duplicates, int Unbound);

/// <summary>
/// Durable, idempotent Meta webhook ingest. The anonymous HTTP boundary may return 200 only after
/// every routed event is stored in its tenant-scoped inbox; unroutable events remain unacknowledged
/// so Meta retries. Legacy pre-5.9.40 inbound keys are checked before inserting the new SHA-256 key,
/// and the database unique index is the final arbiter under concurrent retries.
/// </summary>
public sealed class WhatsAppMetaWebhookIngestService(
    ISqlSugarClientResolver clientResolver,
    IWhatsAppChannelAccountRepository accounts,
    ICurrentTenant currentTenant,
    ILogger<WhatsAppMetaWebhookIngestService> logger)
{
    public async Task<WhatsAppWebhookIngestResult> IngestAsync(
        IReadOnlyList<WhatsAppMetaInboundMessage> messages,
        IReadOnlyList<WhatsAppMetaMessageStatus> statuses,
        string rawJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJson);
        var db = clientResolver.GetClientForEntity(typeof(SysWhatsAppWebhookInbox));
        var accepted = 0;
        var duplicates = 0;
        var unbound = 0;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var account = await accounts.FindByRouteAnyTenantAsync(
                message.WhatsAppBusinessAccountId, message.PhoneNumberId, cancellationToken);
            if (account is null)
            {
                unbound++;
                continue;
            }
            using var tenantScope = currentTenant.Change(account.TenantId);
            var legacyKey = WhatsAppMetaWebhookEventKeyFactory.LegacyInboundMessageKey(
                account.TenantId, message.WhatsAppBusinessAccountId, message.ProviderMessageId);
            if (await ExistsAsync(db, account.TenantId, message.WhatsAppBusinessAccountId, legacyKey, cancellationToken))
            {
                duplicates++;
                continue;
            }
            var eventKey = WhatsAppMetaWebhookEventKeyFactory.ForInboundMessage(message);
            if (await InsertOnceAsync(db, new SysWhatsAppWebhookInbox
            {
                TenantId = account.TenantId,
                WhatsAppBusinessAccountId = message.WhatsAppBusinessAccountId,
                PhoneNumberId = message.PhoneNumberId,
                EventKey = eventKey,
                EventType = WhatsAppMetaWebhookEventKeyFactory.InboundMessageEventType,
                ProviderMessageId = message.ProviderMessageId,
                SenderWhatsAppId = message.SenderWhatsAppId,
                ProviderTimestamp = message.ProviderTimestamp,
                PayloadJson = rawJson,
                Status = WhatsAppWebhookInboxStatus.Pending
            }, account.TenantId, message.WhatsAppBusinessAccountId, eventKey, cancellationToken))
                accepted++;
            else
                duplicates++;
        }

        foreach (var status in statuses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var account = await accounts.FindByRouteAnyTenantAsync(
                status.WhatsAppBusinessAccountId, status.PhoneNumberId, cancellationToken);
            if (account is null)
            {
                unbound++;
                continue;
            }
            using var tenantScope = currentTenant.Change(account.TenantId);
            var eventKey = WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(status);
            if (await InsertOnceAsync(db, new SysWhatsAppWebhookInbox
            {
                TenantId = account.TenantId,
                WhatsAppBusinessAccountId = status.WhatsAppBusinessAccountId,
                PhoneNumberId = status.PhoneNumberId,
                EventKey = eventKey,
                EventType = WhatsAppMetaWebhookEventKeyFactory.MessageStatusEventType,
                ProviderMessageId = status.ProviderMessageId,
                RecipientWhatsAppId = status.RecipientWhatsAppId,
                ProviderTimestamp = status.ProviderTimestamp,
                PayloadJson = rawJson,
                Status = WhatsAppWebhookInboxStatus.Pending
            }, account.TenantId, status.WhatsAppBusinessAccountId, eventKey, cancellationToken))
                accepted++;
            else
                duplicates++;
        }

        logger.LogInformation(
            "Meta webhook ingest: accepted {Accepted}, duplicates {Duplicates}, unbound {Unbound}",
            accepted, duplicates, unbound);
        return new WhatsAppWebhookIngestResult(accepted, duplicates, unbound);
    }

    private static async Task<bool> ExistsAsync(
        ISqlSugarClient db,
        long tenantId,
        string wabaId,
        string eventKey,
        CancellationToken cancellationToken)
        => await db.Queryable<SysWhatsAppWebhookInbox>().ClearFilter()
            .CountAsync(item => item.TenantId == tenantId
                && item.WhatsAppBusinessAccountId == wabaId
                && item.EventKey == eventKey, cancellationToken) > 0;

    private async Task<bool> InsertOnceAsync(
        ISqlSugarClient db,
        SysWhatsAppWebhookInbox row,
        long tenantId,
        string wabaId,
        string eventKey,
        CancellationToken cancellationToken)
    {
        if (await ExistsAsync(db, tenantId, wabaId, eventKey, cancellationToken))
            return false;
        try
        {
            await db.Insertable(row).ExecuteCommandAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && IsUniqueViolation(ex))
        {
            // Concurrent retry lost the uniqueness race: the event is already durably accepted.
            return false;
        }
    }

    private static bool IsUniqueViolation(Exception ex)
        => ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("23505", StringComparison.Ordinal);
}
