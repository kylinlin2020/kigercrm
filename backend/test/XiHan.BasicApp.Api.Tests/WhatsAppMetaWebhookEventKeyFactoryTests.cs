using XiHan.BasicApp.Chat.Infrastructure.Channels;

namespace XiHan.BasicApp.Api.Tests;

public sealed class WhatsAppMetaWebhookEventKeyFactoryTests
{
    [Fact]
    public void InboundMessageKey_ShouldBeDeterministicAndBoundedSha256()
    {
        var message = new WhatsAppMetaInboundMessage(
            "waba-1", "phone-1", "wamid-1", "sender-1",
            DateTimeOffset.FromUnixTimeSeconds(1794000000), "text", "{\"text\":{\"body\":\"hi\"}}");

        var first = WhatsAppMetaWebhookEventKeyFactory.ForInboundMessage(message);
        var second = WhatsAppMetaWebhookEventKeyFactory.ForInboundMessage(message);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.All(first, character => Assert.Contains(character, "0123456789abcdef"));
    }

    [Fact]
    public void LegacyInboundKey_ShouldMatchPreEventMigrationBackfillFormat()
    {
        var expected = Convert.ToHexString(
            System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes("7:waba-1:wamid-1")))
            .ToLowerInvariant();

        Assert.Equal(
            expected,
            WhatsAppMetaWebhookEventKeyFactory.LegacyInboundMessageKey(7, "waba-1", "wamid-1"));
        Assert.Equal(32, expected.Length);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WhatsAppMetaWebhookEventKeyFactory.LegacyInboundMessageKey(0, "waba-1", "wamid-1"));
    }

    [Fact]
    public void LegacyInboundKey_ShouldPreserveStoredIdentityWhitespaceLikeMigrationSql()
    {
        var storedWaba = " waba-1 ";
        var storedMessageId = " wamid-1 ";
        var expected = Convert.ToHexString(
            System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes($"7:{storedWaba}:{storedMessageId}")))
            .ToLowerInvariant();

        Assert.Equal(
            expected,
            WhatsAppMetaWebhookEventKeyFactory.LegacyInboundMessageKey(
                7, storedWaba, storedMessageId));
    }

    [Fact]
    public void MessageAndStatusEventsForSameWamid_ShouldHaveDifferentKeys()
    {
        var message = new WhatsAppMetaInboundMessage(
            "waba-1", "phone-1", "wamid-1", "sender-1",
            DateTimeOffset.FromUnixTimeSeconds(1794000000), "text", "{}");
        var status = new WhatsAppMetaMessageStatus(
            "waba-1", "phone-1", "wamid-1", "recipient-1", "delivered",
            DateTimeOffset.FromUnixTimeSeconds(1794000001), null, "{}");

        Assert.NotEqual(
            WhatsAppMetaWebhookEventKeyFactory.ForInboundMessage(message),
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(status));
    }

    [Fact]
    public void RepeatedSameStatus_ShouldDeduplicateButLaterTransitionShouldNot()
    {
        var delivered = new WhatsAppMetaMessageStatus(
            "waba-1", "phone-1", "wamid-1", "recipient-1", "delivered",
            DateTimeOffset.FromUnixTimeSeconds(1794000001), null, "{}");
        var read = delivered with { ProviderStatus = "read", ProviderTimestamp = DateTimeOffset.FromUnixTimeSeconds(1794000002) };

        Assert.Equal(
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(delivered),
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(delivered with { StatusJson = "{ \"status\": \"delivered\" }" }));
        Assert.NotEqual(
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(delivered),
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(read));
    }

    [Fact]
    public void StatusKey_ShouldCanonicalizeErrorJsonPropertyOrder()
    {
        var first = new WhatsAppMetaMessageStatus(
            "waba-1", "phone-1", "wamid-1", "recipient-1", "failed",
            DateTimeOffset.FromUnixTimeSeconds(1794000001),
            "{\"code\":131000,\"title\":\"Temporary\"}", "{}");
        var second = first with
        {
            ErrorsJson = "{ \"title\" : \"Temporary\", \"code\" : 131000 }"
        };

        Assert.Equal(
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(first),
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(second));
    }

    [Fact]
    public void EventKeyFactory_ShouldRejectMissingIdentity()
    {
        var message = new WhatsAppMetaInboundMessage(
            "waba-1", "phone-1", " ", "sender-1",
            DateTimeOffset.FromUnixTimeSeconds(1794000000), "text", "{}");

        Assert.Throws<ArgumentException>(() => WhatsAppMetaWebhookEventKeyFactory.ForInboundMessage(message));
    }

    [Fact]
    public void StatusKey_ShouldNormalizeCaseAndTrimIdentityFields()
    {
        var first = new WhatsAppMetaMessageStatus(
            " waba-1 ", " phone-1 ", " wamid-1 ", " recipient-1 ", "DELIVERED",
            DateTimeOffset.FromUnixTimeSeconds(1794000001), null, "{}");
        var second = first with
        {
            WhatsAppBusinessAccountId = "waba-1",
            PhoneNumberId = "phone-1",
            ProviderMessageId = "wamid-1",
            RecipientWhatsAppId = "recipient-1",
            ProviderStatus = "delivered"
        };

        Assert.Equal(
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(first),
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(second));
    }

    [Fact]
    public void StatusKey_ShouldDistinguishDifferentProviderTimestamps()
    {
        var first = new WhatsAppMetaMessageStatus(
            "waba-1", "phone-1", "wamid-1", "recipient-1", "delivered",
            DateTimeOffset.FromUnixTimeSeconds(1794000001), null, "{}");
        var later = first with
        {
            ProviderTimestamp = DateTimeOffset.FromUnixTimeSeconds(1794000002)
        };

        Assert.NotEqual(
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(first),
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(later));
    }

    [Fact]
    public void StatusKey_ShouldRejectMalformedErrorJson()
    {
        var status = new WhatsAppMetaMessageStatus(
            "waba-1", "phone-1", "wamid-1", "recipient-1", "failed",
            DateTimeOffset.FromUnixTimeSeconds(1794000001), "{invalid", "{}");

        Assert.Throws<System.Text.Json.JsonException>(
            () => WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(status));
    }


    [Fact]
    public void InboundMessageKey_ShouldNormalizeIdentityWhitespace()
    {
        var first = new WhatsAppMetaInboundMessage(
            " waba-1 ", " phone-1 ", " wamid-1 ", "sender-1",
            DateTimeOffset.FromUnixTimeSeconds(1794000000), "text", "{}");
        var normalized = first with
        {
            WhatsAppBusinessAccountId = "waba-1",
            PhoneNumberId = "phone-1",
            ProviderMessageId = "wamid-1"
        };

        Assert.Equal(
            WhatsAppMetaWebhookEventKeyFactory.ForInboundMessage(first),
            WhatsAppMetaWebhookEventKeyFactory.ForInboundMessage(normalized));
    }

    [Fact]
    public void InboundMessageKey_ShouldSeparateDifferentPhoneNumberBindings()
    {
        var first = new WhatsAppMetaInboundMessage(
            "waba-1", "phone-1", "wamid-1", "sender-1",
            DateTimeOffset.FromUnixTimeSeconds(1794000000), "text", "{}");
        var otherPhone = first with { PhoneNumberId = "phone-2" };

        Assert.NotEqual(
            WhatsAppMetaWebhookEventKeyFactory.ForInboundMessage(first),
            WhatsAppMetaWebhookEventKeyFactory.ForInboundMessage(otherPhone));
    }

    [Fact]
    public void StatusKey_ShouldSeparateDifferentErrorArrays()
    {
        var first = new WhatsAppMetaMessageStatus(
            "waba-1", "phone-1", "wamid-1", "recipient-1", "failed",
            DateTimeOffset.FromUnixTimeSeconds(1794000001),
            "[{\\"code\\":131000,\\"title\\":\\"Temporary\\"}]", "{}");
        var otherError = first with
        {
            ErrorsJson = "[{\\"code\\":131001,\\"title\\":\\"Rejected\\"}]"
        };

        Assert.NotEqual(
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(first),
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(otherError));
    }


    [Fact]
    public void StatusKey_ShouldCanonicalizeNestedErrorArrayObjects()
    {
        var first = new WhatsAppMetaMessageStatus(
            "waba-1", "phone-1", "wamid-1", "recipient-1", "failed",
            DateTimeOffset.FromUnixTimeSeconds(1794000001),
            "[{\\"code\\":131000,\\"details\\":{\\"title\\":\\"Temporary\\",\\"href\\":\\"help\\"}}]", "{}");
        var reordered = first with
        {
            ErrorsJson = "[{\\"details\\":{\\"href\\":\\"help\\",\\"title\\":\\"Temporary\\"},\\"code\\":131000}]"
        };

        Assert.Equal(
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(first),
            WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(reordered));
    }

    [Fact]
    public void StatusKey_ShouldRejectErrorJsonExceedingMaximumDepth()
    {
        var nested = new string('[', 18) + "0" + new string(']', 18);
        var status = new WhatsAppMetaMessageStatus(
            "waba-1", "phone-1", "wamid-1", "recipient-1", "failed",
            DateTimeOffset.FromUnixTimeSeconds(1794000001), nested, "{}");

        Assert.Throws<System.Text.Json.JsonException>(
            () => WhatsAppMetaWebhookEventKeyFactory.ForMessageStatus(status));
    }

}
