using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmShipmentCreationWiringTests
{
    [Fact]
    public void ShipmentCreation_ShouldBeTransactionalAndRequireCreatePermission()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        Assert.Contains("[UnitOfWork(true)]", source, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]", source, StringComparison.Ordinal);
        Assert.Contains("shipments.AddAsync", source, StringComparison.Ordinal);
        Assert.Contains("shipmentLines.AddAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCreation_ShouldValidateOrderScopeLineOwnershipAndQuantity()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        Assert.Contains("EnsureRecordAccessibleAsync", source, StringComparison.Ordinal);
        Assert.Contains("orderLine.SalesOrderId != order.BasicId", source, StringComparison.Ordinal);
        Assert.Contains("requested.Quantity > availableQuantity", source, StringComparison.Ordinal);
        Assert.Contains("Distinct().Count()", source, StringComparison.Ordinal);
        Assert.Contains("CrmSalesOrderStatus.Confirmed", source, StringComparison.Ordinal);
    }


    [Fact]
    public void ShipmentCancellation_ShouldBeExecuteAuthorizedAndDraftOnly()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", source, StringComparison.Ordinal);
        Assert.Contains("shipment.Status != CrmShipmentStatus.Draft", source, StringComparison.Ordinal);
        Assert.Contains("target == CrmShipmentStatus.Cancelled", source, StringComparison.Ordinal);
        Assert.Contains("shipment.Status = target", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync", source, StringComparison.Ordinal);
        Assert.Contains("EnsureUpdatableAsync(typeof(SysCrmShipment)", source, StringComparison.Ordinal);
    }


    [Fact]
    public void ShipmentFulfillment_ShouldReserveReleaseAndConsumeOrderQuantities()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        Assert.Contains("orderLine.ReservedQuantity += requested.Quantity", source, StringComparison.Ordinal);
        Assert.Contains("orderLine.ReservedQuantity -= shipmentLine.Quantity", source, StringComparison.Ordinal);
        Assert.Contains("orderLine.ShippedQuantity += shipmentLine.Quantity", source, StringComparison.Ordinal);
        Assert.Contains("CrmShipmentStatus.Dispatched", source, StringComparison.Ordinal);
        Assert.Contains("CrmShipmentStatus.Delivered", source, StringComparison.Ordinal);
        Assert.Contains("orders.UpdateAsync(order", source, StringComparison.Ordinal);
        Assert.Contains("ListForOrderAsync", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));
    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
