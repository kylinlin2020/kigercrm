using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.BasicApp.Chat.Domain.Prospecting;
using XiHan.BasicApp.Chat.Infrastructure.Channels;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>WeCom gateway: signed callbacks, allowlisted commands, no external send.</summary>
public sealed class WecomGatewayTests
{
    [Fact]
    public void Signature_ShouldBeDeterministicSortedSha1AndRejectMismatches()
    {
        const string token = "preview-token";
        const string timestamp = "1700000000";
        const string nonce = "nonce";
        const string body = "<xml>hello</xml>";
        var expected = Sha1(token, timestamp, nonce, body);

        Assert.True(WecomCallbackSignatureValidator.IsValid(token, timestamp, nonce, body, expected));
        Assert.False(WecomCallbackSignatureValidator.IsValid(token, timestamp, nonce, body, "bad"));
        Assert.False(WecomCallbackSignatureValidator.IsValid(null, timestamp, nonce, body, expected));
        Assert.False(WecomCallbackSignatureValidator.IsValid(token, timestamp, nonce, "<xml>tampered</xml>", expected));
    }

    [Fact]
    public void CommandPolicy_ShouldOnlyReplyToAllowlistedCommands()
    {
        Assert.Equal(WecomCommandOutcome.Replied, WecomCommandPolicy.Evaluate("/help").Outcome);
        Assert.Equal(WecomCommandOutcome.Replied, WecomCommandPolicy.Evaluate("status").Outcome);
        Assert.Equal(WecomCommandOutcome.Replied, WecomCommandPolicy.Evaluate("ping").Outcome);
        var unknown = WecomCommandPolicy.Evaluate("wire money");
        Assert.Equal(WecomCommandOutcome.Ignored, unknown.Outcome);
        Assert.Equal("unknown_command", unknown.Reason);
        Assert.Equal(string.Empty, unknown.Reply);
        Assert.Equal(WecomCommandOutcome.Rejected, WecomCommandPolicy.Evaluate(new string('x', 501)).Outcome);
        Assert.Equal(40, WecomCommandPolicy.HashReply("pong").Length);
    }

    [Fact]
    public void GatewayContract_ShouldFailClosedForEncryptedCallbacksAndAvoidOutboundSend()
    {
        var endpoint = Read("backend", "src", "modules", "XiHan.BasicApp.Chat",
            "Infrastructure", "Channels", "WecomCallbackEndpoints.cs");
        var module = Read("backend", "src", "modules", "XiHan.BasicApp.Chat",
            "XiHanBasicAppChatModule.cs");
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Chat",
            "Application", "AppServices", "WecomGatewayAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.49", "5.9.49.sql");

        Assert.Contains("body.Contains(\"<Encrypt>\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status503ServiceUnavailable", endpoint, StringComparison.Ordinal);
        Assert.Contains("WecomCallbackSignatureValidator.IsValid", endpoint, StringComparison.Ordinal);
        Assert.Contains("MapWecomCallbackEndpoints()", module, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(ChatPermissionCodes.Manage)]", app, StringComparison.Ordinal);
        Assert.DoesNotContain("SendMessage", app, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS sys_wecom_command_audit", migration, StringComparison.Ordinal);
    }

    private static string Sha1(params string[] values)
    {
        Array.Sort(values, StringComparer.Ordinal);
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(string.Concat(values)))).ToLowerInvariant();
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
