// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;

namespace XiHan.BasicApp.Chat.Infrastructure.Channels;

/// <summary>
/// Extracts inbound message envelopes from a signature-verified Meta webhook body.
/// This parser does not resolve tenants or persist messages; callers must route through
/// a server-owned WABA/phone-number binding before writing the tenant-scoped inbox.
/// </summary>
public static class WhatsAppMetaWebhookPayloadParser
{
    /// <summary>Upper bound to prevent an unexpectedly large webhook from exhausting memory.</summary>
    public const int MaxInboundMessagesPerWebhook = 500;

    /// <summary>
    /// Parses all inbound message events. Status-only webhook notifications are valid and
    /// produce an empty collection. A malformed inbound message rejects the whole envelope.
    /// </summary>
    public static bool TryParseInboundMessages(
        string? rawJson,
        out IReadOnlyList<WhatsAppMetaInboundMessage> messages)
    {
        messages = [];
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson, new JsonDocumentOptions
            {
                MaxDepth = 32,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("entry", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var parsed = new List<WhatsAppMetaInboundMessage>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !TryGetString(entry, "id", out var wabaId) ||
                    !entry.TryGetProperty("changes", out var changes) ||
                    changes.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (var change in changes.EnumerateArray())
                {
                    if (change.ValueKind != JsonValueKind.Object ||
                        !change.TryGetProperty("value", out var value) ||
                        value.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    // Delivery/read status updates are not inbound user messages.
                    if (!value.TryGetProperty("messages", out var eventMessages))
                    {
                        continue;
                    }

                    if (eventMessages.ValueKind != JsonValueKind.Array ||
                        !value.TryGetProperty("metadata", out var metadata) ||
                        metadata.ValueKind != JsonValueKind.Object ||
                        !TryGetString(metadata, "phone_number_id", out var phoneNumberId))
                    {
                        return false;
                    }

                    foreach (var message in eventMessages.EnumerateArray())
                    {
                        if (parsed.Count >= MaxInboundMessagesPerWebhook ||
                            message.ValueKind != JsonValueKind.Object ||
                            !TryGetString(message, "id", out var messageId) ||
                            !TryGetString(message, "from", out var senderId) ||
                            !TryGetString(message, "timestamp", out var timestampText) ||
                            !TryGetString(message, "type", out var messageType) ||
                            !long.TryParse(timestampText, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds) ||
                            unixSeconds <= 0)
                        {
                            return false;
                        }

                        DateTimeOffset providerTimestamp;
                        try
                        {
                            providerTimestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            return false;
                        }

                        parsed.Add(new WhatsAppMetaInboundMessage(
                            wabaId,
                            phoneNumberId,
                            messageId,
                            senderId,
                            providerTimestamp,
                            messageType,
                            message.GetRawText()));
                    }
                }
            }

            messages = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses delivery/read/failure receipts separately from inbound messages. Unknown provider
    /// states are retained as opaque bounded strings so the persistence layer can quarantine them
    /// for review instead of silently mapping them to a successful delivery state.
    /// </summary>
    public static bool TryParseStatuses(
        string? rawJson,
        out IReadOnlyList<WhatsAppMetaMessageStatus> statuses)
    {
        statuses = [];
        if (string.IsNullOrWhiteSpace(rawJson))
            return false;

        try
        {
            using var document = JsonDocument.Parse(rawJson, new JsonDocumentOptions
            {
                MaxDepth = 32,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("entry", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
                return false;

            var parsed = new List<WhatsAppMetaMessageStatus>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !TryGetString(entry, "id", out var wabaId) ||
                    !entry.TryGetProperty("changes", out var changes) ||
                    changes.ValueKind != JsonValueKind.Array)
                    return false;

                foreach (var change in changes.EnumerateArray())
                {
                    if (change.ValueKind != JsonValueKind.Object ||
                        !change.TryGetProperty("value", out var value) ||
                        value.ValueKind != JsonValueKind.Object)
                        return false;

                    if (!value.TryGetProperty("statuses", out var eventStatuses))
                        continue;

                    if (eventStatuses.ValueKind != JsonValueKind.Array ||
                        !value.TryGetProperty("metadata", out var metadata) ||
                        metadata.ValueKind != JsonValueKind.Object ||
                        !TryGetString(metadata, "phone_number_id", out var phoneNumberId))
                        return false;

                    foreach (var status in eventStatuses.EnumerateArray())
                    {
                        if (parsed.Count >= MaxInboundMessagesPerWebhook ||
                            status.ValueKind != JsonValueKind.Object ||
                            !TryGetString(status, "id", out var messageId) ||
                            !TryGetString(status, "status", out var state) ||
                            !TryGetString(status, "timestamp", out var timestampText) ||
                            !TryGetString(status, "recipient_id", out var recipientId) ||
                            !long.TryParse(timestampText, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds) ||
                            unixSeconds <= 0)
                            return false;

                        DateTimeOffset providerTimestamp;
                        try
                        {
                            providerTimestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            return false;
                        }

                        var errorJson = status.TryGetProperty("errors", out var errors) &&
                            errors.ValueKind == JsonValueKind.Array
                            ? errors.GetRawText()
                            : null;

                        parsed.Add(new WhatsAppMetaMessageStatus(
                            wabaId,
                            phoneNumberId,
                            messageId,
                            recipientId,
                            state,
                            providerTimestamp,
                            errorJson,
                            status.GetRawText()));
                    }
                }
            }

            statuses = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = property.GetString();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 200)
        {
            return false;
        }

        value = candidate;
        return true;
    }
}

/// <summary>Normalized inbound message metadata extracted from a verified Meta envelope.</summary>
public sealed record WhatsAppMetaInboundMessage(
    string WhatsAppBusinessAccountId,
    string PhoneNumberId,
    string ProviderMessageId,
    string SenderWhatsAppId,
    DateTimeOffset ProviderTimestamp,
    string MessageType,
    string MessageJson);


/// <summary>Normalized delivery/read/failure receipt from a signature-verified Meta webhook.</summary>
public sealed record WhatsAppMetaMessageStatus(
    string WhatsAppBusinessAccountId,
    string PhoneNumberId,
    string ProviderMessageId,
    string RecipientWhatsAppId,
    string ProviderStatus,
    DateTimeOffset ProviderTimestamp,
    string? ErrorsJson,
    string StatusJson);
