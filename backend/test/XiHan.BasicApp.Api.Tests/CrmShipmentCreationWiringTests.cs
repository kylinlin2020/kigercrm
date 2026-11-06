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
        var deliveredStart = source.IndexOf("else\n        {\n            if (shipment.Status != CrmShipmentStatus.Dispatched)", dispatchStart, StringComparison.Ordinal);

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
        Assert.True(cancellation.IndexOf(parentScopeCheck, StringComparison.Ordinal)
            < cancellation.IndexOf("orders.UpdateAsync(order, cancellationToken)", StringComparison.Ordinal));
        Assert.True(dispatch.IndexOf(parentScopeCheck, StringComparison.Ordinal)
            < dispatch.IndexOf("orders.UpdateAsync(order, cancellationToken)", StringComparison.Ordinal));
        Assert.True(cancellation.IndexOf(parentScopeCheck, StringComparison.Ordinal)
            < cancellation.IndexOf("orderLines.UpdateAsync(orderLine, cancellationToken)", StringComparison.Ordinal));
        Assert.True(dispatch.IndexOf(parentScopeCheck, StringComparison.Ordinal)
            < dispatch.IndexOf("orderLines.UpdateAsync(orderLine, cancellationToken)", StringComparison.Ordinal));
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

    [Fact]
    public void ShipmentCreation_ShouldReplaySameRequestBeforeReservingQuantities()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var createStart = source.IndexOf("CreateAsync(CrmShipmentCreateDto", StringComparison.Ordinal);
        var statusStart = source.IndexOf("ChangeStatusAsync(CrmShipmentStatusUpdateDto", createStart, StringComparison.Ordinal);
        var create = source[createStart..statusStart];

        Assert.Contains("CreateRequestFingerprint", create, StringComparison.Ordinal);
        Assert.Contains("GetByShipmentNumberAsync(number, cancellationToken)", create, StringComparison.Ordinal);
        Assert.Contains("existingShipment.CreationRequestFingerprint", create, StringComparison.Ordinal);
        Assert.Contains("return ToDto(existingShipment)", create, StringComparison.Ordinal);
        Assert.Contains("different or unverifiable create request", create, StringComparison.Ordinal);
        Assert.True(create.IndexOf("GetByShipmentNumberAsync", StringComparison.Ordinal)
            < create.IndexOf("order.FulfillmentRevision++", StringComparison.Ordinal));
        Assert.True(create.IndexOf("GetByShipmentNumberAsync", StringComparison.Ordinal)
            < create.IndexOf("orderLine.ReservedQuantity += requested.Quantity", StringComparison.Ordinal));
    }

    [Fact]
    public void ShipmentCreation_ShouldFingerprintNormalizedRequestAndPersistItOnHeader()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmShipment.cs");
        Assert.Contains("SHA256.HashData", source, StringComparison.Ordinal);
        Assert.Contains("SalesOrderLineId, line.Quantity", source, StringComparison.Ordinal);
        Assert.Contains("CreationRequestFingerprint = creationRequestFingerprint", source, StringComparison.Ordinal);
        Assert.Contains("Creation_Request_Fingerprint", entity, StringComparison.Ordinal);
        Assert.Contains("Length = 64", entity, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentDispatch_ShouldFenceOrderAndDeriveFulfillmentFromPersistedLedger()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var methodStart = source.IndexOf("public async Task<CrmShipmentDto> ChangeStatusAsync", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var method = source[methodStart..];
        var dispatchStart = method.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", StringComparison.Ordinal);
        var deliveryStart = method.IndexOf("else\n        {\n            if (shipment.Status != CrmShipmentStatus.Dispatched)", dispatchStart, StringComparison.Ordinal);
        Assert.True(dispatchStart >= 0 && deliveryStart > dispatchStart);
        var dispatch = method[dispatchStart..deliveryStart];

        Assert.Contains("[UnitOfWork(true)]", source[..methodStart], StringComparison.Ordinal);
        Assert.Contains("shipment.Status != CrmShipmentStatus.Draft", dispatch, StringComparison.Ordinal);
        Assert.Contains("orders.UpdateAsync(order, cancellationToken)", dispatch, StringComparison.Ordinal);
        Assert.Contains("GetByIdAsync(order.BasicId, cancellationToken)", dispatch, StringComparison.Ordinal);
        Assert.Contains("orderLine.ReservedQuantity < shipmentLine.Quantity", dispatch, StringComparison.Ordinal);
        Assert.Contains("orderLine.ShippedQuantity + shipmentLine.Quantity > orderLine.Quantity", dispatch, StringComparison.Ordinal);
        Assert.Contains("orderLine.ReservedQuantity -= shipmentLine.Quantity", dispatch, StringComparison.Ordinal);
        Assert.Contains("orderLine.ShippedQuantity += shipmentLine.Quantity", dispatch, StringComparison.Ordinal);
        Assert.Contains("ListForOrderAsync(order.BasicId, cancellationToken)", dispatch, StringComparison.Ordinal);
        Assert.Contains("CrmSalesOrderStatus.Completed", dispatch, StringComparison.Ordinal);
        Assert.Contains("CrmSalesOrderStatus.PartiallyShipped", dispatch, StringComparison.Ordinal);
        Assert.Contains("shipment.Status = target", method, StringComparison.Ordinal);

        var aggregateLock = dispatch.IndexOf("orders.UpdateAsync(order, cancellationToken)", StringComparison.Ordinal);
        var reloadToken = dispatch.IndexOf("GetByIdAsync(order.BasicId, cancellationToken)", aggregateLock, StringComparison.Ordinal);
        var firstLineMutation = dispatch.IndexOf("orderLines.UpdateAsync(orderLine, cancellationToken)", StringComparison.Ordinal);
        var ledgerRead = dispatch.IndexOf("ListForOrderAsync(order.BasicId, cancellationToken)", StringComparison.Ordinal);
        Assert.True(aggregateLock >= 0 && reloadToken > aggregateLock && firstLineMutation > reloadToken && ledgerRead > firstLineMutation,
            "Dispatch must acquire and reload the order aggregate concurrency token before mutating lines, then derive status from the persisted fulfillment ledger.");
    }

    [Fact]
    public void ShipmentCreateRequestLedger_ShouldBeWrittenAfterHeaderAndLinesInsideOneTransaction()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var createStart = source.IndexOf("public async Task<CrmShipmentDto> CreateAsync", StringComparison.Ordinal);
        var statusStart = source.IndexOf("public async Task<CrmShipmentDto> ChangeStatusAsync", createStart, StringComparison.Ordinal);
        Assert.True(createStart >= 0 && statusStart > createStart);

        var create = source[createStart..statusStart];
        var attributeStart = source.LastIndexOf("[UnitOfWork(true)]", createStart, StringComparison.Ordinal);
        Assert.True(attributeStart >= 0, "Shipment creation must execute inside an explicit transactional unit of work.");

        var headerInsert = create.IndexOf("shipments.AddAsync(shipment, cancellationToken)", StringComparison.Ordinal);
        var lineInsert = create.IndexOf("shipmentLines.AddAsync(line, cancellationToken)", StringComparison.Ordinal);
        var requestLedgerInsert = create.LastIndexOf("createRequests.AddAsync(new SysCrmCreateRequest", StringComparison.Ordinal);
        Assert.True(headerInsert >= 0 && lineInsert > headerInsert && requestLedgerInsert > lineInsert,
            "The idempotency ledger must not commit before the shipment header and all shipment lines have been staged.");
        Assert.Contains("PayloadHash = creationRequestFingerprint", create, StringComparison.Ordinal);
        Assert.Contains("DocumentId = saved.BasicId", create, StringComparison.Ordinal);
        Assert.Contains("DocumentNumber = saved.ShipmentNumber", create, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCancellation_ShouldReleaseReservationsWithoutChangingShippedQuantities()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var methodStart = source.IndexOf("public async Task<CrmShipmentDto> ChangeStatusAsync", StringComparison.Ordinal);
        var method = source[methodStart..];
        var cancelStart = method.IndexOf("if (target == CrmShipmentStatus.Cancelled)", StringComparison.Ordinal);
        var dispatchStart = method.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", cancelStart, StringComparison.Ordinal);
        Assert.True(cancelStart >= 0 && dispatchStart > cancelStart);
        var cancellation = method[cancelStart..dispatchStart];

        Assert.Contains("shipment.Status != CrmShipmentStatus.Draft", cancellation, StringComparison.Ordinal);
        Assert.Contains("order.FulfillmentRevision++", cancellation, StringComparison.Ordinal);
        Assert.Contains("orderLine.ReservedQuantity < shipmentLine.Quantity", cancellation, StringComparison.Ordinal);
        Assert.Contains("orderLine.ReservedQuantity -= shipmentLine.Quantity", cancellation, StringComparison.Ordinal);
        Assert.DoesNotContain("orderLine.ShippedQuantity +=", cancellation, StringComparison.Ordinal);
        Assert.DoesNotContain("orderLine.ShippedQuantity -=", cancellation, StringComparison.Ordinal);
        Assert.True(cancellation.IndexOf("orders.UpdateAsync(order, cancellationToken)", StringComparison.Ordinal)
            < cancellation.IndexOf("orderLines.UpdateAsync(orderLine, cancellationToken)", StringComparison.Ordinal),
            "Cancellation must acquire the order aggregate lock before releasing line reservations.");
    }

    [Fact]
    public void ShipmentDelivery_ShouldOnlyCompleteDispatchedShipmentAndSetDeliveryTimestamp()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var methodStart = source.IndexOf("public async Task<CrmShipmentDto> ChangeStatusAsync", StringComparison.Ordinal);
        var method = source[methodStart..];
        var dispatchStart = method.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", StringComparison.Ordinal);
        var deliveryStart = method.IndexOf("else\\n        {\\n            if (shipment.Status != CrmShipmentStatus.Dispatched)", dispatchStart, StringComparison.Ordinal);
        Assert.True(dispatchStart >= 0 && deliveryStart > dispatchStart);
        var delivery = method[deliveryStart..];

        Assert.Contains("shipment.Status != CrmShipmentStatus.Dispatched", delivery, StringComparison.Ordinal);
        Assert.Contains("shipment.DeliveredDate ??= DateTime.UtcNow", delivery, StringComparison.Ordinal);
        Assert.DoesNotContain("orderLine.ReservedQuantity", delivery, StringComparison.Ordinal);
        Assert.DoesNotContain("orderLine.ShippedQuantity", delivery, StringComparison.Ordinal);
        Assert.DoesNotContain("orders.UpdateAsync", delivery, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));
    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
