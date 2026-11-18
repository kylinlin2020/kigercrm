// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace XiHan.BasicApp.Chat.Infrastructure.Channels;

/// <summary>
/// Minimal public HTTP boundary for Meta WhatsApp webhook verification.
/// POST deliberately fails closed until tenant account binding and durable inbox persistence
/// are registered; acknowledging an event before durable storage would lose messages on crashes.
/// </summary>
public static class WhatsAppMetaWebhookEndpoints
{
    private const string ConfigurationPrefix = "XiHan:Chat:WhatsApp:Meta";
    private const int MaxWebhookBodyBytes = 1_048_576;

    /// <summary>Maps the anonymous Meta WhatsApp webhook verification and receipt endpoints.</summary>
    public static IEndpointRouteBuilder MapWhatsAppMetaWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/chat/webhooks/whatsapp/meta", VerifySubscriptionAsync).AllowAnonymous();
        endpoints.MapPost("/api/chat/webhooks/whatsapp/meta", ReceiveWebhookAsync).AllowAnonymous();
        return endpoints;
    }

    private static IResult VerifySubscriptionAsync(HttpRequest request, IConfiguration configuration)
    {
        var expectedToken = configuration[$"{ConfigurationPrefix}:WebhookVerifyToken"];
        if (!WhatsAppMetaWebhookChallengeValidator.TryValidate(
            request.Query["hub.mode"].ToString(),
            request.Query["hub.verify_token"].ToString(),
            expectedToken,
            request.Query["hub.challenge"].ToString(),
            out var challenge))
            return Results.Unauthorized();

        return Results.Text(challenge, "text/plain", Encoding.UTF8);
    }

    private static async Task<IResult> ReceiveWebhookAsync(
        HttpRequest request,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        WhatsAppMetaWebhookIngestService ingestService,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is > MaxWebhookBodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        await using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaxWebhookBodyBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var rawBody = buffer.ToArray();
        var appSecret = configuration[$"{ConfigurationPrefix}:AppSecret"];
        var signature = request.Headers["X-Hub-Signature-256"].ToString();
        if (!WhatsAppMetaWebhookSignatureValidator.IsValid(rawBody, signature, appSecret))
            return Results.Unauthorized();

        string rawJson;
        try
        {
            // Reject invalid UTF-8 instead of silently replacing bytes before JSON parsing.
            rawJson = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(rawBody);
        }
        catch (DecoderFallbackException)
        {
            return Results.BadRequest();
        }

        if (!WhatsAppMetaWebhookPayloadParser.TryParseInboundMessages(rawJson, out var messages) ||
            !WhatsAppMetaWebhookPayloadParser.TryParseStatuses(rawJson, out var statuses))
            return Results.BadRequest();

        var ingest = await ingestService.IngestAsync(messages, statuses, rawJson, cancellationToken);
        if (ingest.Unbound > 0 && ingest.Accepted == 0)
        {
            // No event could be routed to a tenant-owned account: stay fail-closed so Meta retries
            // instead of the application silently acknowledging and losing the message.
            loggerFactory.CreateLogger("WhatsAppMetaWebhookEndpoints").LogWarning(
                "Verified Meta webhook had {Unbound} unrouted event(s); returning 503 for provider retry.",
                ingest.Unbound);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        return Results.Ok(new
        {
            ingest.Accepted,
            ingest.Duplicates,
            ingest.Unbound,
        });
    }
}
