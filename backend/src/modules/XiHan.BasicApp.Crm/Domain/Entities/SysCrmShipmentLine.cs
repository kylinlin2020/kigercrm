using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Quantity fulfilled from one sales-order line by one shipment.</summary>
[SugarTable(TableName = "Crm_Shipment_Line", TableDescription = "KIGER CRM shipment line quantities")]
[SugarIndex("UX_{table}_TeId_Sh_OrLi_IsDe", nameof(TenantId), OrderByType.Asc, nameof(ShipmentId), OrderByType.Asc, nameof(SalesOrderLineId), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_TeId_OrLi", nameof(TenantId), OrderByType.Asc, nameof(SalesOrderLineId), OrderByType.Asc)]
public sealed class SysCrmShipmentLine : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Shipment_Id", IsNullable = false)]
    public long ShipmentId { get; set; }

    [SugarColumn(ColumnName = "Sales_Order_Line_Id", IsNullable = false)]
    public long SalesOrderLineId { get; set; }

    [SugarColumn(ColumnName = "Quantity", ColumnDataType = "decimal(18,4)", IsNullable = false)]
    public decimal Quantity { get; set; }
}
