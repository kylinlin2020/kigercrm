namespace XiHan.BasicApp.Chat.Application.Dtos;

public sealed class BaileysSessionCreateDto
{
    public string DisplayName { get; set; } = string.Empty;
    public string? AuthStateRef { get; set; }
}

public sealed class BaileysSessionTransitionDto
{
    public long SessionId { get; set; }

    /// <summary>Wire value of BaileysSessionStatus.</summary>
    public int TargetStatus { get; set; }
}

public sealed class BaileysSessionDto
{
    public long BasicId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? AuthStateRef { get; set; }
    public int Status { get; set; }
    public int Health { get; set; }
    public DateTimeOffset? QrIssuedAtUtc { get; set; }
    public DateTimeOffset? LastConnectedAtUtc { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}
