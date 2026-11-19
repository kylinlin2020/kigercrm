using SqlSugar;
using XiHan.BasicApp.AI.Domain.Enums;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.Data.SqlSugar.Routing;
using XiHan.Framework.Domain.Entities.Abstracts;

namespace XiHan.BasicApp.AI.Domain.Entities;

/// <summary>
/// Immutable audit of a knowledge document review decision. The version hash captures the exact
/// raw content that was reviewed, so a later edit requires a fresh review before publication.
/// Platform data only (same isolation as <see cref="SysKnowledgeDocument"/>).
/// </summary>
[SugarTable(TableName = "Sys_Knowledge_Document_Review", TableDescription = "Knowledge document review audit")]
[SugarIndex("IX_{table}_Te_Do_Time", nameof(TenantId), OrderByType.Asc, nameof(DocumentId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[PlatformDataSource]
public sealed class SysKnowledgeDocumentReview : BasicAppCreationEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Document_Id", IsNullable = false)]
    public long DocumentId { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public KnowledgeReviewStatus Status { get; set; }

    [SugarColumn(ColumnName = "Version_Hash", Length = 64, IsNullable = false)]
    public string VersionHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Comment", Length = 1000, IsNullable = true)]
    public string? Comment { get; set; }

    [SugarColumn(ColumnName = "Reviewer_Id", IsNullable = true)]
    public long? ReviewerId { get; set; }

    [SugarColumn(ColumnName = "Reviewed_At_Utc", IsNullable = true)]
    public DateTimeOffset? ReviewedAtUtc { get; set; }
}
