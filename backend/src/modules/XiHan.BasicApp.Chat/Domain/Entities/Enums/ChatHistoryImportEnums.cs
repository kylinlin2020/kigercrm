namespace XiHan.BasicApp.Chat.Domain.Entities.Enums;

public enum ChatHistoryImportBatchStatus
{
    Previewed = 0,
    Imported = 1,
    Failed = 2,
}

public enum ChatHistoryImportRowStatus
{
    Pending = 0,
    Imported = 1,
    Duplicate = 2,
    Invalid = 3,
}
