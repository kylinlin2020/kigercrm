using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// Permanent tenant-scoped idempotency record for CRM document creation.
/// This is a creation-only entity: it intentionally has no soft-delete state.
/// </summary>
[SugarTable(TableName = "Crm_Create_Request", TableDescription = "KIGER CRM document creation idempotency ledger")]
[SugarIndex("UX_{table}_TeId_Dt_RqKe", nameof(TenantId), OrderByType.Asc, nameof(DocumentType), OrderByType.Asc, nameof(RequestKey), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_TeId_DcId", nameof(TenantId), OrderByType.Asc, nameof(DocumentId), OrderByType.Asc)]
public sealed class SysCrmCreateRequest : BasicAppCreationEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Document_Type", Length = 32, IsNullable = false)]
    public string DocumentType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Request_Key", Length = 100, IsNullable = false)]
    public string RequestKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Payload_Hash", Length = 64, IsNullable = false)]
    public string PayloadHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Document_Id", IsNullable = true)]
    public long? DocumentId { get; set; }

    [SugarColumn(ColumnName = "Document_Number", Length = 40, IsNullable = true)]
    public string? DocumentNumber { get; set; }
}
