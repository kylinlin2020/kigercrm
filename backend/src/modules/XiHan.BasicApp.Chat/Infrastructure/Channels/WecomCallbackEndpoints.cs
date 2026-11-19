using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.BasicApp.Chat.Domain.Prospecting;
using XiHan.BasicApp.Chat.Domain.Repositories;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Chat.Infrastructure.Channels;

/// <summary>
/// WeCom command callback boundary. Only signed, tenant-routed, unencrypted preview callbacks are
/// accepted (encrypted callbacks fail closed until an explicit decryption key is configured). No
/// external message is sent: the endpoint audits the command and returns a local acknowledgement.
/// </summary>
public static class WecomCallbackEndpoints
{
    private const string ConfigurationPrefix = "XiHan:Chat:WeCom";
    private const int MaxBodyBytes = 1_048_576;

    public static IEndpointRouteBuilder MapWecomCallbackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/chat/webhooks/wecom", VerifyAsync).AllowAnonymous();
        endpoints.MapPost("/api/chat/webhooks/wecom", ReceiveAsync).AllowAnonymous();
        return endpoints;
    }

    private static IResult VerifyAsync(HttpRequest request, IConfiguration configuration)
    {
        var token = configuration[$"{ConfigurationPrefix}:CallbackToken"];
        var valid = WecomCallbackSignatureValidator.IsValid(
            token,
            request.Query["timestamp"].ToString(),
            request.Query["nonce"].ToString(),
            request.Query["echostr"].ToString(),
            request.Query["msg_signature"].ToString());
        return valid ? Results.Text(request.Query["echostr"].ToString(), "text/plain", Encoding.UTF8) : Results.Unauthorized();
    }

    private static async Task<IResult> ReceiveAsync(
        HttpRequest request,
        IConfiguration configuration,
        IWecomBindingRepository bindings,
        IWecomCommandAuditRepository audits,
        ICurrentTenant currentTenant,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var token = configuration[$"{ConfigurationPrefix}:CallbackToken"];
        if (string.IsNullOrWhiteSpace(token))
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        if (request.ContentLength is > MaxBodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaxBodyBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        var body = Encoding.UTF8.GetString(buffer.ToArray());
        if (body.Contains("<Encrypt>", StringComparison.OrdinalIgnoreCase))
        {
            loggerFactory.CreateLogger("WecomCallbackEndpoints").LogWarning(
                "Encrypted WeCom callback received but no decryption key is configured; failing closed.");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (!WecomCallbackSignatureValidator.IsValid(
                token,
                request.Query["timestamp"].ToString(),
                request.Query["nonce"].ToString(),
                body,
                request.Query["msg_signature"].ToString()))
            return Results.Unauthorized();

        XDocument document;
        try
        {
            document = XDocument.Parse(body, LoadOptions.None);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException)
        {
            return Results.BadRequest();
        }
        var root = document.Root;
        if (root is null) return Results.BadRequest();
        var corpId = Element(root, "ToUserName");
        var agentId = Element(root, "AgentID");
        var fromUserId = Element(root, "FromUserName");
        var messageId = Element(root, "MsgId");
        if (string.IsNullOrWhiteSpace(corpId) || string.IsNullOrWhiteSpace(agentId)
            || string.IsNullOrWhiteSpace(fromUserId) || string.IsNullOrWhiteSpace(messageId))
            return Results.BadRequest();

        var binding = await bindings.FindRouteAnyTenantAsync(corpId, agentId, cancellationToken);
        if (binding is null)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        var command = WecomCommandPolicy.Evaluate(Element(root, "Content"));
        using var scope = currentTenant.Change(binding.TenantId);
        var existing = await audits.GetListAsync(item => item.ProviderMessageId == messageId, cancellationToken);
        if (existing.Count == 0)
        {
            await audits.AddAsync(new SysWecomCommandAudit
            {
                TenantId = binding.TenantId,
                CorpId = corpId,
                AgentId = agentId,
                FromUserId = fromUserId,
                ProviderMessageId = messageId,
                Command = command.Command,
                Outcome = command.Outcome,
                Reason = command.Reason,
                ReplyHash = WecomCommandPolicy.HashReply(command.Reply)
            }, cancellationToken);
        }
        return Results.Text("success", "text/plain", Encoding.UTF8);
    }

    private static string Element(XElement root, string name)
        => root.Elements().FirstOrDefault(item => string.Equals(item.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
}
