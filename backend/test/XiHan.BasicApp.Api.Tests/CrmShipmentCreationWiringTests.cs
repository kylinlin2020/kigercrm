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


    [Fact]
    public void ShipmentFulfillment_ShouldRecheckSourceOrderOwnerScopeBeforeMutation()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var cancellationStart = source.IndexOf("if (target == CrmShipmentStatus.Cancelled)", StringComparison.Ordinal);
        var dispatchStart = source.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", cancellationStart, StringComparison.Ordinal);
        var deliveredStart = source.IndexOf("else\\n        {\\n            if (shipment.Status != CrmShipmentStatus.Dispatched)", dispatchStart, StringComparison.Ordinal);

        Assert.True(cancellationStart >= 0);
        Assert.True(dispatchStart > cancellationStart);
        Assert.True(deliveredStart > dispatchStart);

        var cancellation = source[cancellationStart..dispatchStart];
        var dispatch = source[dispatchStart..deliveredStart];
        const string parentScopeCheck = "EnsureRecordAccessibleAsync(dataScope, order.OwnerUserId, cancellationToken)";
        Assert.Contains(parentScopeCheck, cancellation, StringComparison.Ordinal);
        Assert.Contains(parentScopeCheck, dispatch, StringComparison.Ordinal);
        Assert.True(cancellation.IndexOf("GetByIdAsync(shipment.SalesOrderId", StringComparison.Ordinal)
            < cancellation.IndexOf(parentScopeCheck, StringComparison.Ordinal));
        Assert.True(dispatch.IndexOf("GetByIdAsync(shipment.SalesOrderId", StringComparison.Ordinal)
            < dispatch.IndexOf(parentScopeCheck, StringComparison.Ordinal));
    }

    [Fact]
    public void ShipmentStatusChange_ShouldUseArrayLengthWhenCheckingShipmentLines()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        Assert.Contains(".OrderBy(line => line.SalesOrderLineId)\n            .ToArray();\n        if (lines.Length == 0)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentStatusChange_ShouldRejectRepeatedAndOutOfOrderTransitions()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var start = source.IndexOf("public async Task<CrmShipmentDto> ChangeStatusAsync", StringComparison.Ordinal);
        var method = source[start..];
        var cancelStart = method.IndexOf("if (target == CrmShipmentStatus.Cancelled)", StringComparison.Ordinal);
        var dispatchStart = method.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", cancelStart, StringComparison.Ordinal);
        var deliveredStart = method.IndexOf("else\n        {\n            if (shipment.Status != CrmShipmentStatus.Dispatched)", dispatchStart, StringComparison.Ordinal);

        Assert.True(cancelStart >= 0 && dispatchStart > cancelStart && deliveredStart > dispatchStart);
        var cancellation = method[cancelStart..dispatchStart];
        var dispatch = method[dispatchStart..deliveredStart];
        var delivery = method[deliveredStart..];

        Assert.Contains("shipment.Status != CrmShipmentStatus.Draft", cancellation, StringComparison.Ordinal);
        Assert.Contains("shipment.Status != CrmShipmentStatus.Draft", dispatch, StringComparison.Ordinal);
        Assert.Contains("shipment.Status != CrmShipmentStatus.Dispatched", delivery, StringComparison.Ordinal);
        Assert.Contains("shipment.Status = target", method, StringComparison.Ordinal);
    }


    [Fact]
    public void ShipmentCreation_ShouldSupportOptionalIdempotentServerNumbering()
    {
        var dto = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmShipmentCommandDtos.cs");
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        Assert.Contains("NumberingIdempotencyKey", dto, StringComparison.Ordinal);
        Assert.Contains("INumberGenerator numberGenerator", service, StringComparison.Ordinal);
        Assert.Contains("CrmNumberingRuleCodes.Shipment", service, StringComparison.Ordinal);
        Assert.Contains("NumberingScope.Auto", service, StringComparison.Ordinal);
        Assert.Contains('"CRM_SHIPMENT"', service, StringComparison.Ordinal);
        Assert.Contains("allocation.Numbers.Single()", service, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));
    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
