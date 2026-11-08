// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using SqlSugar;
using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.Framework.Domain.Entities.Abstracts;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Guards the durable WhatsApp inbox against cross-tenant and duplicate-delivery regressions.
/// </summary>
public sealed class WhatsAppWebhookInboxEntityContractTests
{
    [Fact]
    public void Inbox_ShouldBeStrictlyTenantScoped()
    {
        Assert.True(typeof(IStrictMultiTenantEntity).IsAssignableFrom(typeof(SysWhatsAppWebhookInbox)));
    }

    [Fact]
    public void Inbox_ShouldHaveTenantScopedEventUniqueness()
    {
        var index = typeof(SysWhatsAppWebhookInbox)
            .GetCustomAttributes(typeof(SugarIndexAttribute), inherit: false)
            .Cast<SugarIndexAttribute>()
            .Single(attribute => attribute.IndexName == "UX_{table}_TeId_WaId_EvKe");

        Assert.True(index.IsUnique);
        Assert.Contains(nameof(SysWhatsAppWebhookInbox.TenantId), index.IndexFields.Keys);
        Assert.Contains(nameof(SysWhatsAppWebhookInbox.WhatsAppBusinessAccountId), index.IndexFields.Keys);
        Assert.Contains(nameof(SysWhatsAppWebhookInbox.EventKey), index.IndexFields.Keys);
    }

    [Fact]
    public void Inbox_ShouldHaveStandardCreationAuditIndexes()
    {
        var indexes = typeof(SysWhatsAppWebhookInbox)
            .GetCustomAttributes(typeof(SugarIndexAttribute), inherit: false)
            .Cast<SugarIndexAttribute>()
            .Select(attribute => attribute.IndexName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("IX_{table}_TeId_CrTi", indexes);
        Assert.Contains("IX_{table}_CrId", indexes);
    }

    [Fact]
    public void Inbox_ShouldRetainReplayAndRetryMetadata()
    {
        var properties = typeof(SysWhatsAppWebhookInbox).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(SysWhatsAppWebhookInbox.PhoneNumberId), properties);
        Assert.Contains(nameof(SysWhatsAppWebhookInbox.EventKey), properties);
        Assert.Contains(nameof(SysWhatsAppWebhookInbox.EventType), properties);
        Assert.Contains(nameof(SysWhatsAppWebhookInbox.SenderWhatsAppId), properties);
        Assert.Contains(nameof(SysWhatsAppWebhookInbox.ProviderTimestamp), properties);
        Assert.Contains(nameof(SysWhatsAppWebhookInbox.PayloadJson), properties);
        Assert.Contains(nameof(SysWhatsAppWebhookInbox.Status), properties);
        Assert.Contains(nameof(SysWhatsAppWebhookInbox.AttemptCount), properties);
        Assert.Contains(nameof(SysWhatsAppWebhookInbox.NextAttemptTime), properties);
        Assert.Contains(nameof(SysWhatsAppWebhookInbox.ProcessedTime), properties);
    }
}
