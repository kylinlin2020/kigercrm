namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmTradeDocumentImportInputDto
{
    public string RowsJson { get; set; } = string.Empty;
    public string? BatchId { get; set; }
    public long? SalesOrderId { get; set; }
}

public sealed class CrmTradeDocumentLinePreviewDto
{
    public string Sku { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Description { get; set; }
    public long? SuggestedOrderLineId { get; set; }
}

public sealed class CrmTradeDocumentPreviewRowDto
{
    public string ExternalId { get; set; } = string.Empty;
    public int DocumentType { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string OrderReference { get; set; } = string.Empty;
    public string? Buyer { get; set; }
    public string? Seller { get; set; }
    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTime? ShipmentDate { get; set; }
    public string? Currency { get; set; }
    public decimal TotalAmount { get; set; }
    public IReadOnlyList<CrmTradeDocumentLinePreviewDto> Lines { get; set; } = [];
    public IReadOnlyList<string> Errors { get; set; } = [];
    public string? DuplicateReason { get; set; }
}

public sealed class CrmTradeDocumentPreviewDto
{
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Duplicates { get; set; }
    public IReadOnlyList<CrmTradeDocumentPreviewRowDto> Rows { get; set; } = [];
}

public sealed class CrmTradeDocumentImportResultDto
{
    public string BatchId { get; set; } = string.Empty;
    public int Staged { get; set; }
    public int Invalid { get; set; }
    public int DuplicateStaged { get; set; }
}

public sealed class CrmTradeDocumentMappedLineDto
{
    public long OrderLineId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class CrmTradeDocumentConfirmDto
{
    public long RowId { get; set; }
    public long SalesOrderId { get; set; }
    public IReadOnlyList<CrmTradeDocumentMappedLineDto> Lines { get; set; } = [];
}

public sealed class CrmTradeDocumentDecisionDto
{
    public long RowId { get; set; }
    public string? Reason { get; set; }
}

public sealed class CrmTradeDocumentLineDto
{
    public string Sku { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Description { get; set; }
}

public sealed class CrmTradeDocumentRowDto
{
    public long BasicId { get; set; }
    public string BatchId { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public int DocumentType { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string OrderReference { get; set; } = string.Empty;
    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTime? ShipmentDate { get; set; }
    public string? Currency { get; set; }
    public decimal TotalAmount { get; set; }
    public IReadOnlyList<CrmTradeDocumentLineDto> Lines { get; set; } = [];
    public int Status { get; set; }
    public string? DuplicateReason { get; set; }
    public string? RejectReason { get; set; }
    public long? CreatedShipmentId { get; set; }
}

public sealed class CrmTradeDocumentOrderLineDto
{
    public long BasicId { get; set; }
    public string? ProductCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal AvailableQuantity { get; set; }
}
