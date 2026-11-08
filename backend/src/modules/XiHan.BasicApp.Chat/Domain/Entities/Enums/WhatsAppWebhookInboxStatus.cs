// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace XiHan.BasicApp.Chat.Domain.Entities;

/// <summary>Durable inbound WhatsApp webhook processing states.</summary>
public static class WhatsAppWebhookInboxStatus
{
    /// <summary>Accepted and waiting for a tenant-scoped consumer.</summary>
    public const string Pending = "Pending";

    /// <summary>Processing failed transiently and may be retried.</summary>
    public const string Retryable = "Retryable";

    /// <summary>Inbound event was projected successfully.</summary>
    public const string Processed = "Processed";

    /// <summary>Payload is validly signed but unsupported or permanently invalid.</summary>
    public const string NeedsReview = "NeedsReview";
}
