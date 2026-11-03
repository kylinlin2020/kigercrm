namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

/// <summary>Lifecycle state of a confirmed customer sales order.</summary>
public enum CrmSalesOrderStatus
{
    Draft = 0,
    Confirmed = 1,
    Processing = 2,
    PartiallyShipped = 3,
    Completed = 4,
    Cancelled = 5
}
