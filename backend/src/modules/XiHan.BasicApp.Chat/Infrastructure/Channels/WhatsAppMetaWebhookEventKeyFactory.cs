// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XiHan.BasicApp.Chat.Infrastructure.Channels;

/// <summary>
/// Creates deterministic event-level idempotency keys for Meta webhook items.
/// The database uniqueness scope supplies tenant and WABA; this key distinguishes
/// inbound messages from individual status transitions for the same provider message.
/// </summary>
public static class WhatsAppMetaWebhookEventKeyFactory
{
    public const string InboundMessageEventType = "InboundMessage";
    public const string MessageStatusEventType = "MessageStatus";

    public static string ForInboundMessage(WhatsAppMetaInboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Hash(
            InboundMessageEventType,
            Required(message.WhatsAppBusinessAccountId, nameof(message.WhatsAppBusinessAccountId)),
            Required(message.PhoneNumberId, nameof(message.PhoneNumberId)),
            Required(message.ProviderMessageId, nameof(message.ProviderMessageId)));
    }

    /// <summary>
    /// Reproduces the pre-event-key inbox identity used by migration 5.9.40.
    /// A future inbox writer must check this legacy key before inserting a new SHA-256
    /// inbound key, otherwise webhook retries for rows created before migration 5.9.40
    /// can be accepted a second time after the key-format upgrade.
    /// </summary>
    public static string LegacyInboundMessageKey(
        long tenantId,
        string whatsappBusinessAccountId,
        string providerMessageId)
    {
        if (tenantId <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId));

        // Migration 5.9.40 hashes the stored column values verbatim. Do not trim here:
        // even though new event keys normalize identity fields, compatibility lookup must
        // reproduce the exact bytes used for legacy rows.
        if (string.IsNullOrWhiteSpace(whatsappBusinessAccountId))
            throw new ArgumentException("Event identity fields must be non-empty.", nameof(whatsappBusinessAccountId));
        if (string.IsNullOrWhiteSpace(providerMessageId))
            throw new ArgumentException("Event identity fields must be non-empty.", nameof(providerMessageId));

        var identity = string.Concat(
            tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ":",
            whatsappBusinessAccountId,
            ":",
            providerMessageId);
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    public static string ForMessageStatus(WhatsAppMetaMessageStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return Hash(
            MessageStatusEventType,
            Required(status.WhatsAppBusinessAccountId, nameof(status.WhatsAppBusinessAccountId)),
            Required(status.PhoneNumberId, nameof(status.PhoneNumberId)),
            Required(status.ProviderMessageId, nameof(status.ProviderMessageId)),
            Required(status.RecipientWhatsAppId, nameof(status.RecipientWhatsAppId)),
            Required(status.ProviderStatus, nameof(status.ProviderStatus)).ToLowerInvariant(),
            status.ProviderTimestamp.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            CanonicalizeOptionalJson(status.ErrorsJson));
    }

    private static string Hash(params string[] values)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var value in values)
                writer.Write(value);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Event identity fields must be non-empty.", parameterName);
        return value.Trim();
    }

    private static string CanonicalizeOptionalJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(document.RootElement, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
