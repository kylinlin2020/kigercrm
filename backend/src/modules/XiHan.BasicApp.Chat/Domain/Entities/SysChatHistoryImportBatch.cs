using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.Framework.Domain.Entities.Abstracts;

namespace XiHan.BasicApp.Chat.Domain.Entities;

/// <summary>
/// Auditable history-import batch. Rows stay staged for review: the platform does not fabricate
/// conversations or inject historical messages into active threads without an explicit projection
/// step (not implemented in this slice).
/// </summary>
[SugarTable(TableName = "Sys_Chat_History_Import_Batch", TableDescription = "Chat history import batches")]
[SugarIndex("IX_{table}_Te_CrTi", nameof(TenantId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
public sealed class SysChatHistoryImportBatch : BasicAppCreationEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "File_Name", Length = 260, IsNullable = false)]
    public string FileName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Source_Channel", Length = 40, IsNullable = false)]
    public string SourceChannel { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Total_Count", IsNullable = false, DefaultValue = "0")]
    public int TotalCount { get; set; }

    [SugarColumn(ColumnName = "Imported_Count", IsNullable = false, DefaultValue = "0")]
    public int ImportedCount { get; set; }

    [SugarColumn(ColumnName = "Duplicate_Count", IsNullable = false, DefaultValue = "0")]
    public int DuplicateCount { get; set; }

    [SugarColumn(ColumnName = "Invalid_Count", IsNullable = false, DefaultValue = "0")]
    public int InvalidCount { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public ChatHistoryImportBatchStatus Status { get; set; }
}
