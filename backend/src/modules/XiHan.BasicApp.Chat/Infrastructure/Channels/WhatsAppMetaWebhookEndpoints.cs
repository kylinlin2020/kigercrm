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

        var rawJson = Encoding.UTF8.GetString(rawBody);
        if (!WhatsAppMetaWebhookPayloadParser.TryParseInboundMessages(rawJson, out var messages))
            return Results.BadRequest();

        // Do not return 200 here. Until a server-owned WABA/phone_number_id -> tenant binding
        // and atomic unique-key inbox insert are available, the event is not durably accepted.
        // Meta should retry rather than the application silently acknowledging and losing it.
        loggerFactory.CreateLogger("WhatsAppMetaWebhookEndpoints").LogWarning(
            "Verified Meta webhook received {MessageCount} messages, but durable inbox routing is not configured; returning 503 for provider retry.",
            messages.Count);
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
