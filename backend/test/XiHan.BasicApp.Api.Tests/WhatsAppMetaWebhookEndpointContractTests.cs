using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Guards the public Meta webhook boundary against accidental early acknowledgement or route omission.
/// Runtime HTTP tests still require the configured Meta test app and durable inbox implementation.
/// </summary>
public sealed class WhatsAppMetaWebhookEndpointContractTests
{
    [Fact]
    public void ChatModule_ShouldMapAnonymousMetaWebhookGetAndPostRoutes()
    {
        var root = RepositoryRoot();
        var endpointSource = File.ReadAllText(Path.Combine(
            root, "backend", "src", "modules", "XiHan.BasicApp.Chat",
            "Infrastructure", "Channels", "WhatsAppMetaWebhookEndpoints.cs"));
        var moduleSource = File.ReadAllText(Path.Combine(
            root, "backend", "src", "modules", "XiHan.BasicApp.Chat",
            "XiHanBasicAppChatModule.cs"));

        Assert.Contains("MapGet(\"/api/chat/webhooks/whatsapp/meta\", VerifySubscriptionAsync).AllowAnonymous()", endpointSource, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/api/chat/webhooks/whatsapp/meta\", ReceiveWebhookAsync).AllowAnonymous()", endpointSource, StringComparison.Ordinal);
        Assert.Contains("endpoints.MapWhatsAppMetaWebhookEndpoints()", moduleSource, StringComparison.Ordinal);
        Assert.Contains("services.AddChatFieldSecurityEntities()", moduleSource, StringComparison.Ordinal);

        var registrationSource = File.ReadAllText(Path.Combine(
            root, "backend", "src", "modules", "XiHan.BasicApp.Chat",
            "Extensions", "ServiceCollectionExtensions.cs"));
        Assert.Contains("Add<SysWhatsAppWebhookInbox>()", registrationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PostBoundary_ShouldVerifyRawSignatureBoundBodyAndFailClosedBeforeDurableInbox()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "backend", "src", "modules", "XiHan.BasicApp.Chat",
            "Infrastructure", "Channels", "WhatsAppMetaWebhookEndpoints.cs"));

        Assert.Contains("MaxWebhookBodyBytes = 1_048_576", source, StringComparison.Ordinal);
        Assert.Contains("throwOnInvalidBytes: true", source, StringComparison.Ordinal);
        Assert.Contains("catch (DecoderFallbackException)", source, StringComparison.Ordinal);
        Assert.Contains("WhatsAppMetaWebhookSignatureValidator.IsValid(rawBody, signature, appSecret)", source, StringComparison.Ordinal);
        Assert.Contains("WhatsAppMetaWebhookPayloadParser.TryParseInboundMessages(rawJson, out var messages)", source, StringComparison.Ordinal);
        Assert.Contains("WhatsAppMetaWebhookPayloadParser.TryParseStatuses(rawJson, out var statuses)", source, StringComparison.Ordinal);
        Assert.Contains("Status503ServiceUnavailable", source, StringComparison.Ordinal);
        Assert.Contains("Do not return 200 here", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
