using XiHan.BasicApp.Core.Dtos;

namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmShipmentPageQueryDto : BasicAppPRDto
{
    public string? Keyword { get; set; }
    public long? SalesOrderId { get; set; }
    public int? Status { get; set; }
    public long? OwnerUserId { get; set; }
}

public class CrmShipmentListItemDto : BasicAppDto
{
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

public sealed class CrmShipmentDetailDto : CrmShipmentListItemDto
{
    public string? Notes { get; set; }
}

public sealed class CrmShipmentLinePageQueryDto : BasicAppPRDto
{
    public long ShipmentId { get; set; }
}

public sealed class CrmShipmentLineDto : BasicAppDto
{
    public long SalesOrderLineId { get; set; }
    public decimal Quantity { get; set; }
}
