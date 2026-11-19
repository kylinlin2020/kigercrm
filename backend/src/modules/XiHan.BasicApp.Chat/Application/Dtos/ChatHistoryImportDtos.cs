namespace XiHan.BasicApp.Chat.Application.Dtos;

public sealed class ChatHistoryImportRowDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;
}

public sealed class ChatHistoryImportInputDto
{
    public string FileName { get; set; } = string.Empty;
    public string SourceChannel { get; set; } = string.Empty;
    public IReadOnlyList<ChatHistoryImportRowDto> Rows { get; set; } = [];
}

public sealed class ChatHistoryImportVerdictDto
{
    public string ExternalId { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }
    public string ContentHash { get; set; } = string.Empty;
}

public sealed class ChatHistoryImportResultDto
{
    public long BatchId { get; set; }
    public int Total { get; set; }
    public int Imported { get; set; }
    public int Duplicates { get; set; }
    public int Invalid { get; set; }
    public IReadOnlyList<ChatHistoryImportVerdictDto> Rows { get; set; } = [];
}

public sealed class ChatHistoryImportBatchDto
{
    public long BasicId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string SourceChannel { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int ImportedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int InvalidCount { get; set; }
    public int Status { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}
