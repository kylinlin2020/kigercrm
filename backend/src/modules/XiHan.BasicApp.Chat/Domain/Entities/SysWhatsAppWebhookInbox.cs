// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.Domain.Entities.Abstracts;

namespace XiHan.BasicApp.Chat.Domain.Entities;

/// <summary>
/// Meta WhatsApp webhook durable inbox. EventKey is the event-level idempotency boundary; ProviderMessageId remains a correlation field because one message can have many status events.
/// Raw payload is retained for replay/audit; processing is always tenant-scoped.
/// </summary>
[SugarTable(TableName = "Sys_WhatsApp_Webhook_Inbox", TableDescription = "WhatsApp Webhook 持久收件箱")]
[SugarIndex("UX_{table}_TeId_WaId_EvKe", nameof(TenantId), OrderByType.Asc, nameof(WhatsAppBusinessAccountId), OrderByType.Asc, nameof(EventKey), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_TeId_CrTi", nameof(TenantId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[SugarIndex("IX_{table}_CrId", nameof(CreatedId), OrderByType.Asc)]
[SugarIndex("IX_{table}_TeId_St_NeAt", nameof(TenantId), OrderByType.Asc, nameof(Status), OrderByType.Asc, nameof(NextAttemptTime), OrderByType.Asc)]
public partial class SysWhatsAppWebhookInbox : BasicAppCreationEntity, IStrictMultiTenantEntity
{
    /// <summary>Meta WABA ID used to route the event to a tenant-owned channel account.</summary>
    [SugarColumn(ColumnName = "WhatsApp_Business_Account_Id", Length = 100, IsNullable = false)]
    public virtual string WhatsAppBusinessAccountId { get; set; } = string.Empty;

    /// <summary>Meta phone_number_id within the WABA.</summary>
    [SugarColumn(ColumnName = "Phone_Number_Id", Length = 100, IsNullable = false)]
    public virtual string PhoneNumberId { get; set; } = string.Empty;

    /// <summary>Provider's globally unique WhatsApp message ID (wamid), the idempotency key.</summary>
    [SugarColumn(ColumnName = "Provider_Message_Id", Length = 200, IsNullable = false)]
    public virtual string ProviderMessageId { get; set; } = string.Empty;

    /// <summary>Sender's WhatsApp address as received from Meta.</summary>
    [SugarColumn(ColumnName = "Sender_Wa_Id", Length = 100, IsNullable = false)]
    public virtual string SenderWhatsAppId { get; set; } = string.Empty;

    /// <summary>Provider timestamp, preserved as UTC.</summary>
    [SugarColumn(ColumnName = "Provider_Timestamp", IsNullable = false)]
    public virtual DateTimeOffset ProviderTimestamp { get; set; }

    /// <summary>Raw verified webhook envelope, retained for deterministic replay.</summary>
    [SugarColumn(ColumnName = "Payload_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public virtual string PayloadJson { get; set; } = string.Empty;

    /// <summary>Durable processing state; only Pending/Retryable rows are eligible for a consumer.</summary>
    [SugarColumn(ColumnName = "Status", Length = 24, IsNullable = false)]
    public virtual string Status { get; set; } = "Pending";

    /// <summary>Number of processing attempts.</summary>
    [SugarColumn(ColumnName = "Attempt_Count", IsNullable = false)]
    public virtual int AttemptCount { get; set; }

    /// <summary>Next eligible retry time in UTC.</summary>
    [SugarColumn(ColumnName = "Next_Attempt_Time", IsNullable = true)]
    public virtual DateTimeOffset? NextAttemptTime { get; set; }

    /// <summary>Time the inbound event was successfully projected into Chat/CRM.</summary>
    [SugarColumn(ColumnName = "Processed_Time", IsNullable = true)]
    public virtual DateTimeOffset? ProcessedTime { get; set; }

    /// <summary>Sanitized last processing error; never store access tokens or webhook secrets here.</summary>
    [SugarColumn(ColumnName = "Last_Error", Length = 1000, IsNullable = true)]
    public virtual string? LastError { get; set; }
}
