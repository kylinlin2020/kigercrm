// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.Chat.Infrastructure.Channels;

namespace XiHan.BasicApp.Api.Tests;

public sealed class WhatsAppMetaWebhookPayloadParserTests
{
    [Fact]
    public void TryParseInboundMessages_ShouldExtractMessageAndProviderRoutingMetadata()
    {
        const string payload = """
            {
              "object": "whatsapp_business_account",
              "entry": [{
                "id": "waba-123",
                "changes": [{
                  "field": "messages",
                  "value": {
                    "metadata": { "phone_number_id": "phone-456" },
                    "messages": [{
                      "from": "15551234567",
                      "id": "wamid.example.1",
                      "timestamp": "1794000000",
                      "type": "text",
                      "text": { "body": "Hello" }
                    }]
                  }
                }]
              }]
            }
            """;

        var parsed = WhatsAppMetaWebhookPayloadParser.TryParseInboundMessages(payload, out var messages);

        Assert.True(parsed);
        var message = Assert.Single(messages);
        Assert.Equal("waba-123", message.WhatsAppBusinessAccountId);
        Assert.Equal("phone-456", message.PhoneNumberId);
        Assert.Equal("wamid.example.1", message.ProviderMessageId);
        Assert.Equal("15551234567", message.SenderWhatsAppId);
        Assert.Equal("text", message.MessageType);
        Assert.Contains("Hello", message.MessageJson, StringComparison.Ordinal);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794000000), message.ProviderTimestamp);
    }

    [Fact]
    public void TryParseInboundMessages_ShouldAcceptStatusOnlyWebhookWithoutMessages()
    {
        const string payload = """
            {
              "object": "whatsapp_business_account",
              "entry": [{
                "id": "waba-123",
                "changes": [{
                  "field": "messages",
                  "value": {
                    "statuses": [{ "id": "wamid.example.1", "status": "delivered" }
                    ]
                  }
                }]
              }]
            }
            """;

        Assert.True(WhatsAppMetaWebhookPayloadParser.TryParseInboundMessages(payload, out var messages));
        Assert.Empty(messages);
    }

    [Fact]
    public void TryParseStatuses_ShouldExtractDeliveryReadAndFailureReceipts()
    {
        const string payload = """
            {
              "entry": [{
                "id": "waba-123",
                "changes": [{
                  "field": "messages",
                  "value": {
                    "metadata": { "phone_number_id": "phone-456" },
                    "statuses": [
                      { "id": "wamid.example.1", "status": "delivered", "timestamp": "1794000000", "recipient_id": "15551234567" },
                      { "id": "wamid.example.2", "status": "read", "timestamp": "1794000001", "recipient_id": "15551234568" },
                      { "id": "wamid.example.3", "status": "failed", "timestamp": "1794000002", "recipient_id": "15551234569", "errors": [{ "code": 131047, "title": "Re-engagement message" }] }
                    ]
                  }
                }]
              }]
            }
            """;

        Assert.True(WhatsAppMetaWebhookPayloadParser.TryParseStatuses(payload, out var statuses));
        Assert.Equal(3, statuses.Count);
        Assert.Equal("waba-123", statuses[0].WhatsAppBusinessAccountId);
        Assert.Equal("phone-456", statuses[0].PhoneNumberId);
        Assert.Equal("wamid.example.1", statuses[0].ProviderMessageId);
        Assert.Equal("15551234567", statuses[0].RecipientWhatsAppId);
        Assert.Equal("delivered", statuses[0].ProviderStatus);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794000000), statuses[0].ProviderTimestamp);
        Assert.Contains("131047", statuses[2].ErrorsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseStatuses_ShouldPreserveUnknownStatesForQuarantineRatherThanAssumeSuccess()
    {
        const string payload = """
            {"entry":[{"id":"waba","changes":[{"value":{
              "metadata":{"phone_number_id":"phone"},
              "statuses":[{"id":"wamid.1","status":"future_state","timestamp":"1794000000","recipient_id":"15551234567"}]
            }}]}]}
            """;

        Assert.True(WhatsAppMetaWebhookPayloadParser.TryParseStatuses(payload, out var statuses));
        Assert.Equal("future_state", Assert.Single(statuses).ProviderStatus);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"entry\":[{\"id\":\"waba\",\"changes\":[{\"value\":{\"metadata\":{\"phone_number_id\":\"phone\"},\"statuses\":[{\"id\":\"m\",\"status\":\"delivered\",\"timestamp\":\"0\",\"recipient_id\":\"r\"}]}}]}]}")]
    [InlineData("{\"entry\":[{\"id\":\"waba\",\"changes\":[{\"value\":{\"statuses\":[{\"id\":\"m\",\"status\":\"delivered\",\"timestamp\":\"1794000000\",\"recipient_id\":\"r\"}]}}]}]}")]
    public void TryParseStatuses_ShouldRejectMalformedReceipts(string payload)
    {
        Assert.False(WhatsAppMetaWebhookPayloadParser.TryParseStatuses(payload, out var statuses));
        Assert.Empty(statuses);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"entry\": []}")]
    [InlineData("{\"entry\":[{\"id\":\"waba\",\"changes\":[{\"value\":{\"metadata\":{\"phone_number_id\":\"p\"},\"messages\":[{\"id\":\"m\",\"from\":\"s\",\"timestamp\":\"0\",\"type\":\"text\"}]}}]}]}")]
    public void TryParseInboundMessages_ShouldRejectMalformedOrIncompleteInboundEnvelope(string payload)
    {
        Assert.False(WhatsAppMetaWebhookPayloadParser.TryParseInboundMessages(payload, out var messages));
        Assert.Empty(messages);
    }

    [Fact]
    public void TryParseInboundMessages_ShouldRejectOversizedMessageBatch()
    {
        var messages = string.Join(",", Enumerable.Range(0, WhatsAppMetaWebhookPayloadParser.MaxInboundMessagesPerWebhook + 1)
            .Select(index => $$"""{"id":"wamid.{{index}}","from":"15550000000","timestamp":"1794000000","type":"text"}"""));
        var payload = $$$"""
            {"entry":[{"id":"waba","changes":[{"value":{"metadata":{"phone_number_id":"phone"},"messages":[{{{messages}}}]}}]}]}
            """;

        Assert.False(WhatsAppMetaWebhookPayloadParser.TryParseInboundMessages(payload, out var parsed));
        Assert.Empty(parsed);
    }

    [Theory]
    [InlineData("{\"entry\":[{\"id\":\"waba\",\"changes\":[{\"value\":{\"metadata\":{\"phone_number_id\":\"phone\"},\"statuses\":[{\"id\":\"m\",\"status\":\"failed\",\"timestamp\":\"1794000000\",\"recipient_id\":\"r\",\"errors\":{}}]}}]}]}")]
    [InlineData("{\"entry\":[{\"id\":\"waba\",\"changes\":[{\"value\":{\"metadata\":{\"phone_number_id\":\"phone\"},\"statuses\":[{\"id\":\"m\",\"status\":\"failed\",\"timestamp\":\"1794000000\",\"recipient_id\":\"r\",\"errors\":[\"not-an-error-object\"]}]}}]}]}")]
    [InlineData("{\"entry\":[{\"id\":\"waba\",\"changes\":[{\"value\":{\"metadata\":{\"phone_number_id\":\"phone\"},\"statuses\":[{\"id\":\"m\",\"status\":\"failed\",\"timestamp\":\"1794000000\",\"recipient_id\":\"r\",\"errors\":null}]}}]}]}")]
    public void TryParseStatuses_ShouldRejectMalformedErrorPayloadShape(string payload)
    {
        Assert.False(WhatsAppMetaWebhookPayloadParser.TryParseStatuses(payload, out var statuses));
        Assert.Empty(statuses);
    }


    [Fact]
    public void TryParseStatuses_ShouldRejectErrorPayloadBeyondCanonicalizationDepth()
    {
        var nestedErrors = new string('[', 17) + "{}" + new string(']', 17);
        var payload = $$$"""
            {"entry":[{"id":"waba","changes":[{"value":{
              "metadata":{"phone_number_id":"phone"},
              "statuses":[{"id":"m","status":"failed","timestamp":"1794000000","recipient_id":"r","errors":{{{nestedErrors}}}}]
            }}]}]}
            """;

        Assert.False(WhatsAppMetaWebhookPayloadParser.TryParseStatuses(payload, out var statuses));
        Assert.Empty(statuses);
    }

}
