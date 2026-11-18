// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.BasicApp.Chat.Infrastructure.Channels;
using XiHan.BasicApp.Chat.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>M4: Meta webhook acknowledgement only after durable, tenant-routed, idempotent inbox writes.</summary>
public sealed class WhatsappWebhookIngestTests : IDisposable
{
    private const long TenantId = 9981;
    private const string Waba = "waba-1";
    private const string Phone = "phone-1";

    private readonly string _connectionString = $"Data Source=kiger-wa-ingest-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly WhatsAppChannelAccountRepository _accounts;
    private readonly WhatsAppMetaWebhookIngestService _ingest;

    public WhatsappWebhookIngestTests()
    {
        _tenantScope = _tenant.Change(TenantId);
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        _db = new SqlSugarScope(
            new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = _connectionString,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            },
            db =>
            {
                db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(entity => ScopeId() == entity.TenantId);
                db.Aop.DataExecuting = (_, entityInfo) =>
                {
                    if (entityInfo.OperationType == DataFilterType.InsertByObject)
                        entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                };
            });
        _db.CodeFirst.InitTables(typeof(SysWhatsAppChannelAccount), typeof(SysWhatsAppWebhookInbox));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _accounts = new WhatsAppChannelAccountRepository(_resolver.Object);
        _ingest = new WhatsAppMetaWebhookIngestService(
            _resolver.Object,
            _accounts,
            _tenant,
            new Mock<ILogger<WhatsAppMetaWebhookIngestService>>().Object);
    }

    [Fact]
    public async Task Ingest_ShouldRouteAndDurablyStoreMessagesAndStatuses()
    {
        await SeedBindingAsync();
        var messages = new[] { Inbound("wamid-1") };
        var statuses = new[] { Status("wamid-1") };

        var result = await _ingest.IngestAsync(messages, statuses, "{\"entry\":[]}");

        Assert.Equal(2, result.Accepted);
        Assert.Equal(0, result.Unbound);
        var rows = await _db.Queryable<SysWhatsAppWebhookInbox>().ClearFilter().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(TenantId, row.TenantId));
        Assert.Contains(rows, row => row.EventType == WhatsAppMetaWebhookEventKeyFactory.InboundMessageEventType
            && row.EventKey == WhatsAppMetaWebhookEventKeyFactory.ForInboundMessage(messages[0]));
        Assert.Contains(rows, row => row.EventType == WhatsAppMetaWebhookEventKeyFactory.MessageStatusEventType
            && row.EventKey == WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(statuses[0]));
        Assert.All(rows, row => Assert.Equal(WhatsAppWebhookInboxStatus.Pending, row.Status));
    }

    [Fact]
    public async Task Ingest_ShouldBeIdempotentOnProviderRetry()
    {
        await SeedBindingAsync();
        var messages = new[] { Inbound("wamid-1") };
        var statuses = new[] { Status("wamid-1") };

        Assert.Equal(2, (await _ingest.IngestAsync(messages, statuses, "{}")).Accepted);
        var replay = await _ingest.IngestAsync(messages, statuses, "{}");

        Assert.Equal(0, replay.Accepted);
        Assert.Equal(2, replay.Duplicates);
        Assert.Equal(2, await _db.Queryable<SysWhatsAppWebhookInbox>().ClearFilter().CountAsync());
    }

    [Fact]
    public async Task Ingest_ShouldStayFailClosedForUnroutedEvents()
    {
        var result = await _ingest.IngestAsync(new[] { Inbound("wamid-x") }, [], "{}");
        Assert.Equal(0, result.Accepted);
        Assert.Equal(1, result.Unbound);
        Assert.Equal(0, await _db.Queryable<SysWhatsAppWebhookInbox>().ClearFilter().CountAsync());
    }

    [Fact]
    public async Task Ingest_ShouldHonorLegacyInboundKeyBeforeNewKeyFormat()
    {
        await SeedBindingAsync();
        var message = Inbound("wamid-legacy");
        var legacyKey = WhatsAppMetaWebhookEventKeyFactory.LegacyInboundMessageKey(TenantId, Waba, message.ProviderMessageId);
        await _db.Insertable(new SysWhatsAppWebhookInbox
        {
            TenantId = TenantId,
            WhatsAppBusinessAccountId = Waba,
            PhoneNumberId = Phone,
            EventKey = legacyKey,
            EventType = WhatsAppMetaWebhookEventKeyFactory.InboundMessageEventType,
            ProviderMessageId = message.ProviderMessageId,
            ProviderTimestamp = message.ProviderTimestamp,
            PayloadJson = "legacy",
            Status = WhatsAppWebhookInboxStatus.Pending
        }).ExecuteCommandAsync();

        var result = await _ingest.IngestAsync([message], [], "{}");
        Assert.Equal(0, result.Accepted);
        Assert.Equal(1, result.Duplicates);
        Assert.Equal(1, await _db.Queryable<SysWhatsAppWebhookInbox>().ClearFilter().CountAsync());
    }

    [Fact]
    public void WebhookContract_ShouldOnlyAcknowledgeAfterDurableIngest()
    {
        var endpoint = Read("backend", "src", "modules", "XiHan.BasicApp.Chat",
            "Infrastructure", "Channels", "WhatsAppMetaWebhookEndpoints.cs");
        var ingest = Read("backend", "src", "modules", "XiHan.BasicApp.Chat",
            "Infrastructure", "Channels", "WhatsAppMetaWebhookIngestService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.47", "5.9.47.sql");
        Assert.Contains("ingestService.IngestAsync", endpoint, StringComparison.Ordinal);
        Assert.Contains("ingest.Unbound > 0 && ingest.Accepted == 0", endpoint, StringComparison.Ordinal);
        Assert.Contains("Results.Ok(new", endpoint, StringComparison.Ordinal);
        Assert.Contains("LegacyInboundMessageKey", ingest, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS sys_whatsapp_channel_account", migration, StringComparison.Ordinal);
    }

    private async Task SeedBindingAsync()
        => await _accounts.AddAsync(new SysWhatsAppChannelAccount
        {
            TenantId = TenantId,
            DisplayName = "preview",
            WhatsAppBusinessAccountId = Waba,
            PhoneNumberId = Phone,
            CredentialRef = "vault://whatsapp/preview",
            Status = WhatsAppChannelAccountStatus.Active
        });

    private static WhatsAppMetaInboundMessage Inbound(string messageId)
        => new(Waba, Phone, messageId, "sender-1",
            new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero), "text", "{}");

    private static WhatsAppMetaMessageStatus Status(string messageId)
        => new(Waba, Phone, messageId, "sender-1", "delivered",
            new DateTimeOffset(2026, 11, 18, 10, 1, 0, TimeSpan.Zero), null, "{}");

    public void Dispose()
    {
        _tenantScope.Dispose();
        AsyncLocalCurrentTenantAccessor.Instance.Current = null;
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static long ScopeId() => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
