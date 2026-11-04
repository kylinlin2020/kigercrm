using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Shipment header recording physical fulfillment independently from the order lifecycle.</summary>
[SugarTable(TableName = "Crm_Shipment", TableDescription = "KIGER CRM shipment records")]
[SugarIndex("UX_{table}_TeId_No", nameof(TenantId), OrderByType.Asc, nameof(ShipmentNumber), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_TeId_Or_Cr", nameof(TenantId), OrderByType.Asc, nameof(SalesOrderId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
public sealed class SysCrmShipment : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Shipment_Number", Length = 40, IsNullable = false)]
    public string ShipmentNumber { get; set; } = string.Empty;

    /// <summary>SHA-256 fingerprint of the normalized create request when server-numbered idempotency is used.</summary>
    [SugarColumn(ColumnName = "Creation_Request_Fingerprint", Length = 64, IsNullable = true)]
    public string? CreationRequestFingerprint { get; set; }

    [SugarColumn(ColumnName = "Sales_Order_Id", IsNullable = false)]
    public long SalesOrderId { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false)]
    public CrmShipmentStatus Status { get; set; } = CrmShipmentStatus.Draft;

    [SugarColumn(ColumnName = "Shipment_Date", IsNullable = true)]
    public DateTime? ShipmentDate { get; set; }

    [SugarColumn(ColumnName = "Delivered_Date", IsNullable = true)]
    public DateTime? DeliveredDate { get; set; }

    [SugarColumn(ColumnName = "Carrier", Length = 100, IsNullable = true)]
    public string? Carrier { get; set; }

    [SugarColumn(ColumnName = "Tracking_Number", Length = 120, IsNullable = true)]
    public string? TrackingNumber { get; set; }

    [SugarColumn(ColumnName = "Notes", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? Notes { get; set; }

    [SugarColumn(ColumnName = "Owner_User_Id", IsNullable = true)]
    public long? OwnerUserId { get; set; }
}
