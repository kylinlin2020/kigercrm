namespace XiHan.BasicApp.Chat.Application.Dtos;

/// <summary>
/// Binding input. CredentialRef is a reference into the tenant secret store, never a token or
/// app secret, so it cannot leak through logs or API responses.
/// </summary>
public sealed class WhatsAppChannelBindingDto
{
    public string DisplayName { get; set; } = string.Empty;
    public string WhatsAppBusinessAccountId { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string? CredentialRef { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class WhatsAppChannelAccountDto
{
    public long BasicId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string WhatsAppBusinessAccountId { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string? CredentialRef { get; set; }
    public int Status { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}

public sealed class WhatsAppWebhookInboxDto
{
    public long BasicId { get; set; }
    public string WhatsAppBusinessAccountId { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string ProviderMessageId { get; set; } = string.Empty;
    public string? SenderWhatsAppId { get; set; }
    public string? RecipientWhatsAppId { get; set; }
    public DateTimeOffset ProviderTimestamp { get; set; }
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTimeOffset? ProcessedTime { get; set; }
    public string? LastError { get; set; }
}
