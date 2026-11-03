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
        Assert.Contains("order.FulfillmentRevision++", source, StringComparison.Ordinal);
        Assert.Contains("orders.UpdateAsync(order", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCancellation_ShouldAdvanceTheSourceOrderFulfillmentRevision()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var cancellationStart = source.IndexOf("if (target == CrmShipmentStatus.Cancelled)", StringComparison.Ordinal);
        var dispatchStart = source.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", cancellationStart, StringComparison.Ordinal);

        Assert.True(cancellationStart >= 0);
        Assert.True(dispatchStart > cancellationStart);
        var cancellationBranch = source[cancellationStart..dispatchStart];
        Assert.Contains("order.FulfillmentRevision++", cancellationBranch, StringComparison.Ordinal);
        Assert.Contains("orders.UpdateAsync(order, cancellationToken)", cancellationBranch, StringComparison.Ordinal);
    }


    [Fact]
    public void ShipmentFulfillment_ShouldAcquireOrderLineLocksInStableKeyOrder()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        Assert.Contains("input.Lines.OrderBy(line => line.SalesOrderLineId).ToArray()", source, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(line => line.SalesOrderLineId)", source, StringComparison.Ordinal);
        Assert.Contains("var requestedLines = input.Lines.OrderBy", source, StringComparison.Ordinal);
        Assert.Contains("GetByShipmentIdAsync(shipment.BasicId, cancellationToken))", source, StringComparison.Ordinal);

        var createStart = source.IndexOf("public async Task<CrmShipmentDto> CreateAsync", StringComparison.Ordinal);
        var statusStart = source.IndexOf("public async Task<CrmShipmentDto> ChangeStatusAsync", createStart, StringComparison.Ordinal);
        Assert.True(createStart >= 0);
        Assert.True(statusStart > createStart);
        var create = source[createStart..statusStart];
        Assert.True(create.IndexOf("orders.UpdateAsync(order, cancellationToken)", StringComparison.Ordinal)
            < create.IndexOf("orderLines.UpdateAsync(orderLine, cancellationToken)", StringComparison.Ordinal));
    }

    [Fact]
    public void ShipmentFulfillment_ShouldLockOrderBeforeMutatingOrderLines()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var cancellationStart = source.IndexOf("if (target == CrmShipmentStatus.Cancelled)", StringComparison.Ordinal);
        var dispatchStart = source.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", cancellationStart, StringComparison.Ordinal);
        var deliveredStart = source.IndexOf("else\n        {\n            if (shipment.Status != CrmShipmentStatus.Dispatched)", dispatchStart, StringComparison.Ordinal);

        Assert.True(cancellationStart >= 0);
        Assert.True(dispatchStart > cancellationStart);
        Assert.True(deliveredStart > dispatchStart);

        var cancellation = source[cancellationStart..dispatchStart];
        var dispatch = source[dispatchStart..deliveredStart];
        Assert.True(cancellation.IndexOf("orders.UpdateAsync(order, cancellationToken)", StringComparison.Ordinal)
            < cancellation.IndexOf("orderLines.UpdateAsync(orderLine, cancellationToken)", StringComparison.Ordinal));
        Assert.True(dispatch.IndexOf("orders.UpdateAsync(order, cancellationToken)", StringComparison.Ordinal)
            < dispatch.IndexOf("orderLines.UpdateAsync(orderLine, cancellationToken)", StringComparison.Ordinal));
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));
    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
