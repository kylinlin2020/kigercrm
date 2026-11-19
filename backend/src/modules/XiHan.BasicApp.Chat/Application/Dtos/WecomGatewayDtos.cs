namespace XiHan.BasicApp.Chat.Application.Dtos;

public sealed class WecomBindingInputDto
{
    public string DisplayName { get; set; } = string.Empty;
    public string CorpId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string? CredentialRef { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class WecomBindingDto
{
    public long BasicId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string CorpId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string? CredentialRef { get; set; }
    public int Status { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}

public sealed class WecomCommandAuditDto
{
    public long BasicId { get; set; }
    public string CorpId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string FromUserId { get; set; } = string.Empty;
    public string ProviderMessageId { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public int Outcome { get; set; }
    public string? Reason { get; set; }
    public string ReplyHash { get; set; } = string.Empty;
}
