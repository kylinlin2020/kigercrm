using XiHan.BasicApp.Chat.Infrastructure.Channels;

namespace XiHan.BasicApp.Api.Tests;

public sealed class WhatsAppMetaWebhookChallengeValidatorTests
{
    [Fact]
    public void TryValidate_ShouldReturnChallengeOnlyForMatchingSubscribeRequest()
    {
        var valid = WhatsAppMetaWebhookChallengeValidator.TryValidate(
            "subscribe", "verify-token", "verify-token", "challenge-123", out var response);

        Assert.True(valid);
        Assert.Equal("challenge-123", response);
    }

    [Theory]
    [InlineData("unsubscribe", "verify-token", "verify-token", "challenge")]
    [InlineData("subscribe", "wrong-token", "verify-token", "challenge")]
    [InlineData("subscribe", null, "verify-token", "challenge")]
    [InlineData("subscribe", "verify-token", null, "challenge")]
    [InlineData("subscribe", "verify-token", "verify-token", null)]
    [InlineData("Subscribe", "verify-token", "verify-token", "challenge")]
    public void TryValidate_ShouldRejectInvalidHandshake(
        string? mode, string? supplied, string? expected, string? challenge)
    {
        var valid = WhatsAppMetaWebhookChallengeValidator.TryValidate(
            mode, supplied, expected, challenge, out var response);

        Assert.False(valid);
        Assert.Empty(response);
    }

    [Fact]
    public void TryValidate_ShouldRejectControlCharactersAndOversizedChallenge()
    {
        Assert.False(WhatsAppMetaWebhookChallengeValidator.TryValidate(
            "subscribe", "token", "token", "bad\nchallenge", out _));
        Assert.False(WhatsAppMetaWebhookChallengeValidator.TryValidate(
            "subscribe", "token", "token", new string('x', 2049), out _));
    }
}
