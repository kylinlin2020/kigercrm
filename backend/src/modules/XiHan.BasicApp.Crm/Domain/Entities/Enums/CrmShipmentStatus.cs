namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

/// <summary>Fulfillment lifecycle for a shipment against a sales order.</summary>
public enum CrmShipmentStatus
{
    Draft = 0,
    Dispatched = 1,
    Delivered = 2,
    Cancelled = 3
}
