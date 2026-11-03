namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmShipmentLineCreateDto
{
    public long SalesOrderLineId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class CrmShipmentCreateDto
{
    public string ShipmentNumber { get; set; } = string.Empty;
    public long SalesOrderId { get; set; }
    public DateTime? ShipmentDate { get; set; }
    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }
    public long? OwnerUserId { get; set; }
    public string? Notes { get; set; }
    public List<CrmShipmentLineCreateDto> Lines { get; set; } = [];
}

public sealed class CrmShipmentDto
{
    public long BasicId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public long SalesOrderId { get; set; }
    public int Status { get; set; }
    public DateTime? ShipmentDate { get; set; }
    public DateTime? DeliveredDate { get; set; }
    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }
    public long? OwnerUserId { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
}

public sealed class CrmShipmentStatusUpdateDto
{
    public long Id { get; set; }
    public int Status { get; set; }
}
