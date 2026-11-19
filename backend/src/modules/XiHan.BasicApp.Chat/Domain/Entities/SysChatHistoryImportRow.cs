using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.Framework.Domain.Entities.Abstracts;

namespace XiHan.BasicApp.Chat.Domain.Entities;

/// <summary>One staged history row with explicit validation/dedupe verdict and content hash only.</summary>
[SugarTable(TableName = "Sys_Chat_History_Import_Row", TableDescription = "Chat history import rows")]
[SugarIndex("IX_{table}_Te_Ba_St", nameof(TenantId), OrderByType.Asc, nameof(BatchId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
[SugarIndex("UX_{table}_Te_Ba_Ex_IsDe", nameof(TenantId), OrderByType.Asc, nameof(BatchId), OrderByType.Asc, nameof(ExternalId), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
public sealed class SysChatHistoryImportRow : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Batch_Id", IsNullable = false)]
    public long BatchId { get; set; }

    [SugarColumn(ColumnName = "Source_Channel", Length = 40, IsNullable = false)]
    public string SourceChannel { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "External_Id", Length = 200, IsNullable = false)]
    public string ExternalId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Sender", Length = 200, IsNullable = false)]
    public string Sender { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Recipient", Length = 200, IsNullable = false)]
    public string Recipient { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Content_Hash", Length = 64, IsNullable = false)]
    public string ContentHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Sent_At_Utc", IsNullable = false)]
    public DateTimeOffset SentAtUtc { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public ChatHistoryImportRowStatus Status { get; set; }

    [SugarColumn(ColumnName = "Reason", Length = 300, IsNullable = true)]
    public string? Reason { get; set; }
}
