using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// One operator-mapped trade-document row. Confirmation delegates to the existing shipment
/// creation contract; the OCR payload itself never creates a shipment automatically.
/// </summary>
[SugarTable(TableName = "Crm_Trade_Document_Import_Row", TableDescription = "CRM trade document OCR import rows")]
[SugarIndex("IX_{table}_Te_Ba_St", nameof(TenantId), OrderByType.Asc, nameof(BatchId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
[SugarIndex("IX_{table}_Te_Key", nameof(TenantId), OrderByType.Asc, nameof(NormalizedDocumentKey), OrderByType.Asc)]
public sealed class SysCrmTradeDocumentImportRow : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Batch_Id", Length = 80, IsNullable = false)]
    public string BatchId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "External_Id", Length = 80, IsNullable = false)]
    public string ExternalId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Document_Type", IsNullable = false, DefaultValue = "0")]
    public CrmTradeDocumentType DocumentType { get; set; }

    [SugarColumn(ColumnName = "Document_Number", Length = 120, IsNullable = false)]
    public string DocumentNumber { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Order_Reference", Length = 120, IsNullable = false)]
    public string OrderReference { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Buyer", Length = 200, IsNullable = true)]
    public string? Buyer { get; set; }

    [SugarColumn(ColumnName = "Seller", Length = 200, IsNullable = true)]
    public string? Seller { get; set; }

    [SugarColumn(ColumnName = "Carrier", Length = 120, IsNullable = true)]
    public string? Carrier { get; set; }

    [SugarColumn(ColumnName = "Tracking_Number", Length = 120, IsNullable = true)]
    public string? TrackingNumber { get; set; }

    [SugarColumn(ColumnName = "Shipment_Date", IsNullable = true)]
    public DateTime? ShipmentDate { get; set; }

    [SugarColumn(ColumnName = "Currency", Length = 3, IsNullable = true)]
    public string? Currency { get; set; }

    [SugarColumn(ColumnName = "Total_Amount", ColumnDataType = "decimal(18,2)", IsNullable = false, DefaultValue = "0")]
    public decimal TotalAmount { get; set; }

    [SugarColumn(ColumnName = "Lines_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string LinesJson { get; set; } = "[]";

    [SugarColumn(ColumnName = "Raw_Ocr_Text", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? RawOcrText { get; set; }

    [SugarColumn(ColumnName = "Normalized_Document_Key", Length = 160, IsNullable = false)]
    public string NormalizedDocumentKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmTradeDocumentImportStatus Status { get; set; }

    [SugarColumn(ColumnName = "Sales_Order_Id", IsNullable = true)]
    public long? SalesOrderId { get; set; }

    [SugarColumn(ColumnName = "Mapped_Lines_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? MappedLinesJson { get; set; }

    [SugarColumn(ColumnName = "Duplicate_Reason", Length = 500, IsNullable = true)]
    public string? DuplicateReason { get; set; }

    [SugarColumn(ColumnName = "Reject_Reason", Length = 1000, IsNullable = true)]
    public string? RejectReason { get; set; }

    [SugarColumn(ColumnName = "Created_Shipment_Id", IsNullable = true)]
    public long? CreatedShipmentId { get; set; }
}
