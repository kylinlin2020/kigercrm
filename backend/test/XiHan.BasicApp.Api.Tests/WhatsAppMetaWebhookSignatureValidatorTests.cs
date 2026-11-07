using System.Security.Cryptography;
using System.Text;
using XiHan.BasicApp.Chat.Infrastructure.Channels;

namespace XiHan.BasicApp.Api.Tests;

public sealed class WhatsAppMetaWebhookSignatureValidatorTests
{
    [Fact]
    public void IsValid_ShouldAcceptExactRawBodyHmacSha256()
    {
        var body = Encoding.UTF8.GetBytes("{\"object\":\"whatsapp_business_account\"}");
        const string secret = "test-app-secret";
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        var header = "sha256=" + Convert.ToHexString(digest).ToLowerInvariant();

        Assert.True(WhatsAppMetaWebhookSignatureValidator.IsValid(body, header, secret));
    }

    [Theory]
    [InlineData(null, "secret")]
    [InlineData("", "secret")]
    [InlineData("sha1=abcd", "secret")]
    [InlineData("sha256=xyz", "secret")]
    [InlineData("sha256=", "secret")]
    [InlineData("sha256=0000000000000000000000000000000000000000000000000000000000000000", null)]
    [InlineData("sha256=0000000000000000000000000000000000000000000000000000000000000000", "")]
    public void IsValid_ShouldRejectMalformedHeadersAndMissingSecrets(string? header, string? secret)
    {
        Assert.False(WhatsAppMetaWebhookSignatureValidator.IsValid(
            Encoding.UTF8.GetBytes("{}"), header, secret));
    }

    [Fact]
    public void IsValid_ShouldRejectModifiedBodyAndDigest()
    {
        var body = Encoding.UTF8.GetBytes("{\"entry\":[]}");
        const string secret = "test-app-secret";
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        var header = "sha256=" + Convert.ToHexString(digest);

        Assert.False(WhatsAppMetaWebhookSignatureValidator.IsValid(
            Encoding.UTF8.GetBytes("{\"entry\":[ ]}"), header, secret));
        Assert.False(WhatsAppMetaWebhookSignatureValidator.IsValid(body,
            "sha256=" + new string('0', 64), secret));
    }

    [Fact]
    public void IsValid_ShouldRejectEmptyBody()
    {
        Assert.False(WhatsAppMetaWebhookSignatureValidator.IsValid(
            ReadOnlySpan<byte>.Empty, "sha256=" + new string('0', 64), "secret"));
    }
}
