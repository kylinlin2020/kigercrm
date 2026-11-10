using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level guards for direct sales order creation.</summary>
public sealed class CrmSalesOrderCreateWiringTests
{
    [Fact]
    public void CreateCommand_ShouldBeTransactionalAndRequireCreatePermission()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var method = source[source.IndexOf("CreateAsync(CrmSalesOrderCreateDto", StringComparison.Ordinal)..source.IndexOf("private static SysCrmSalesOrderLine CalculateLine", StringComparison.Ordinal)];
        Assert.Contains("[UnitOfWork(true)]", source, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]", method, StringComparison.Ordinal);
        Assert.Contains("repository.AddAsync(order", method, StringComparison.Ordinal);
        Assert.Contains("orderLines.AddAsync(line", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCommand_ShouldValidateTenantRelatedRecordsAndTheirOwnerScopes()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var method = source[source.IndexOf("CreateAsync(CrmSalesOrderCreateDto", StringComparison.Ordinal)..source.IndexOf("private static SysCrmSalesOrderLine CalculateLine", StringComparison.Ordinal)];
        Assert.Contains("customers.GetByIdAsync", method, StringComparison.Ordinal);
        Assert.Contains("contacts.GetByIdAsync", method, StringComparison.Ordinal);
        Assert.Contains("opportunities.GetByIdAsync", method, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, customer.OwnerUserId", method, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, contact.OwnerUserId", method, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, opportunity.OwnerUserId", method, StringComparison.Ordinal);
        Assert.Contains("contact.CustomerId != customer.BasicId", method, StringComparison.Ordinal);
        Assert.Contains("opportunity.CustomerId != customer.BasicId", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCommand_ShouldCalculateAmountsOnServerAndProtectCreatedFields()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var method = source[source.IndexOf("CreateAsync(CrmSalesOrderCreateDto", StringComparison.Ordinal)..source.IndexOf("private static SysCrmSalesOrderLine CalculateLine", StringComparison.Ordinal)];
        Assert.Contains("calculated.Sum(x => x.LineSubtotal)", method, StringComparison.Ordinal);
        Assert.Contains("calculated.Sum(x => x.LineDiscount)", method, StringComparison.Ordinal);
        Assert.Contains("calculated.Sum(x => x.LineTax)", method, StringComparison.Ordinal);
        Assert.Contains("calculated.Sum(x => x.LineTotal)", method, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmSalesOrder)", method, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmSalesOrderLine)", source, StringComparison.Ordinal);
        Assert.Contains("CrmSalesOrderStatus.Draft", method, StringComparison.Ordinal);
        Assert.Contains("Lines.Count > 500", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCreate_ShouldReserveOnlyRemainingOrderQuantityUnderOrderLock()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var method = source[source.IndexOf("CreateAsync(CrmShipmentCreateDto", StringComparison.Ordinal)..source.IndexOf("ChangeStatusAsync(CrmShipmentStatusUpdateDto", StringComparison.Ordinal)];
        Assert.Contains("order.FulfillmentRevision++", method, StringComparison.Ordinal);
        Assert.Contains("orderLine.Quantity - orderLine.ReservedQuantity - orderLine.ShippedQuantity", method, StringComparison.Ordinal);
        Assert.Contains("requested.Quantity > availableQuantity", method, StringComparison.Ordinal);
        Assert.Contains("orderLine.ReservedQuantity += requested.Quantity", method, StringComparison.Ordinal);
        Assert.Contains("OrderBy(line => line.SalesOrderLineId)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentLifecycle_ShouldReleaseReservationsOnCancelAndConsumeThemOnDispatch()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var cancel = source[source.IndexOf("if (target == CrmShipmentStatus.Cancelled)", StringComparison.Ordinal)..source.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", StringComparison.Ordinal)];
        var dispatch = source[source.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", StringComparison.Ordinal)..source.IndexOf("else\n        {", source.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", StringComparison.Ordinal), StringComparison.Ordinal)];
        Assert.Contains("orderLine.ReservedQuantity -= shipmentLine.Quantity", cancel, StringComparison.Ordinal);
        Assert.Contains("orderLine.ReservedQuantity -= shipmentLine.Quantity", dispatch, StringComparison.Ordinal);
        Assert.Contains("orderLine.ShippedQuantity += shipmentLine.Quantity", dispatch, StringComparison.Ordinal);
        Assert.Contains("CrmSalesOrderStatus.Completed", dispatch, StringComparison.Ordinal);
        Assert.Contains("CrmSalesOrderStatus.PartiallyShipped", dispatch, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftUpdate_ShouldRequireUpdatePermissionAndRejectNonDraftOrders()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var start = source.IndexOf("UpdateDraftAsync(CrmSalesOrderUpdateDto", StringComparison.Ordinal);
        var end = source.IndexOf("private static SysCrmSalesOrderLine CalculateLine", start, StringComparison.Ordinal);
        var method = source[start..end];
        Assert.Contains("[UnitOfWork(true)]", method, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Update)]", method, StringComparison.Ordinal);
        Assert.Contains("order.Status != CrmSalesOrderStatus.Draft", method, StringComparison.Ordinal);
        Assert.Contains("existing.ReservedQuantity > 0 || existing.ShippedQuantity > 0", method, StringComparison.Ordinal);
        Assert.Contains("Existing order line IDs must be unique and new lines must use ID zero", method, StringComparison.Ordinal);
        Assert.Contains("An edited order line does not belong to the selected order", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftUpdate_ShouldRecalculateTotalsAndPreserveExistingLineIdentity()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var start = source.IndexOf("UpdateDraftAsync(CrmSalesOrderUpdateDto", StringComparison.Ordinal);
        var end = source.IndexOf("private static SysCrmSalesOrderLine CalculateLine", start, StringComparison.Ordinal);
        var method = source[start..end];
        Assert.Contains("existing.LineSubtotal = calculated.LineSubtotal", method, StringComparison.Ordinal);
        Assert.Contains("existing.ReservedQuantity", method, StringComparison.Ordinal);
        Assert.Contains("updatedLines.Sum(x => x.LineTotal)", method, StringComparison.Ordinal);
        Assert.Contains("EnsureUpdatableAsync(typeof(SysCrmSalesOrderLine)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftUpdate_ShouldKeepQuotationDerivedOrderLineSnapshotsImmutable()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var start = source.IndexOf("UpdateDraftAsync(CrmSalesOrderUpdateDto", StringComparison.Ordinal);
        var end = source.IndexOf("private static SysCrmSalesOrderLine CalculateLine", start, StringComparison.Ordinal);
        var method = source[start..end];
        Assert.Contains("existing.SourceQuoteLineId.HasValue", method, StringComparison.Ordinal);
        Assert.Contains("Quotation-derived order lines are immutable", method, StringComparison.Ordinal);
        Assert.Contains("revise the quotation and create a new order", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftUpdate_ShouldCompareQuotationSnapshotFieldsAgainstPersistedSourceMarker()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var dto = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmSalesOrderUpdateDto.cs");
        var start = service.IndexOf("UpdateDraftAsync(CrmSalesOrderUpdateDto", StringComparison.Ordinal);
        var end = service.IndexOf("private static SysCrmSalesOrderLine CalculateLine", start, StringComparison.Ordinal);
        var method = service[start..end];
        Assert.Contains("existing.SourceQuoteLineId.HasValue", method, StringComparison.Ordinal);
        Assert.Contains("existing.UnitPrice != lineInput.UnitPrice", method, StringComparison.Ordinal);
        Assert.Contains("existing.Quantity != lineInput.Quantity", method, StringComparison.Ordinal);
        Assert.Contains("SourceQuoteLineId", dto, StringComparison.Ordinal);
        Assert.Contains("Quotation-derived order lines are immutable", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftUpdate_ShouldPreserveQuoteLinkedCustomerContextAndCurrency()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var start = source.IndexOf("UpdateDraftAsync(CrmSalesOrderUpdateDto", StringComparison.Ordinal);
        var end = source.IndexOf("private static SysCrmSalesOrderLine CalculateLine", start, StringComparison.Ordinal);
        var method = source[start..end];
        Assert.Contains("order.SourceQuoteId.HasValue", method, StringComparison.Ordinal);
        Assert.Contains("input.CustomerId != order.CustomerId", method, StringComparison.Ordinal);
        Assert.Contains("input.ContactId != order.ContactId", method, StringComparison.Ordinal);
        Assert.Contains("input.OpportunityId != order.OpportunityId", method, StringComparison.Ordinal);
        Assert.Contains("currency, order.Currency", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftUpdate_ShouldSoftDeleteOnlyUnreservedManualLinesAndAddNewLines()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var start = service.IndexOf("UpdateDraftAsync(CrmSalesOrderUpdateDto", StringComparison.Ordinal);
        var end = service.IndexOf("private static SysCrmSalesOrderLine CalculateLine", start, StringComparison.Ordinal);
        var method = service[start..end];
        Assert.Contains("var addedLineCount = input.Lines.Count(x => x.Id == 0)", method, StringComparison.Ordinal);
        Assert.Contains("removedLines.Any(x => x.ReservedQuantity > 0 || x.ShippedQuantity > 0)", method, StringComparison.Ordinal);
        Assert.Contains("removedLines.Any(x => x.SourceQuoteLineId.HasValue)", method, StringComparison.Ordinal);
        Assert.Contains("shipmentLines.AnyForSalesOrderLineAsync(removed.BasicId, cancellationToken)", method, StringComparison.Ordinal);
        Assert.Contains("including lines from cancelled shipments", method, StringComparison.Ordinal);
        Assert.Contains("await orderLines.SoftDeleteAsync(removed, cancellationToken)", method, StringComparison.Ordinal);
        Assert.Contains("await orderLines.AddAsync(added, cancellationToken)", method, StringComparison.Ordinal);
        Assert.Contains("order.SourceQuoteId.HasValue && (removedLines.Length > 0 || addedLineCount > 0)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesOrderLineRepository_ShouldUseAuditedSoftDeleteImplementation()
    {
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Repositories", "ICrmSalesOrderLineRepository.cs");
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmSalesOrderLineRepository.cs");
        Assert.Contains("ISoftDeleteRepositoryBase<SysCrmSalesOrderLine, long>", contract, StringComparison.Ordinal);
        Assert.Contains("SqlSugarSoftDeleteRepository<SysCrmSalesOrderLine, long>", repository, StringComparison.Ordinal);
        Assert.Contains("CreateNoTenantQueryable()", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void FulfillmentMutations_ShouldUseAuditedRowVersionedAggregateUpdates()
    {
        var orderEntity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmSalesOrder.cs");
        var orderLineEntity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmSalesOrderLine.cs");
        var shipmentEntity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmShipment.cs");
        var shipmentLineEntity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmShipmentLine.cs");
        var orderService = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var shipmentService = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        Assert.Contains(": BasicAppFullAuditedEntity", orderEntity, StringComparison.Ordinal);
        Assert.Contains(": BasicAppFullAuditedEntity", orderLineEntity, StringComparison.Ordinal);
        Assert.Contains(": BasicAppFullAuditedEntity", shipmentEntity, StringComparison.Ordinal);
        Assert.Contains(": BasicAppFullAuditedEntity", shipmentLineEntity, StringComparison.Ordinal);
        Assert.Contains("repository.UpdateAsync(order, cancellationToken)", orderService, StringComparison.Ordinal);
        Assert.Contains("orders.UpdateAsync(order, cancellationToken)", shipmentService, StringComparison.Ordinal);
        Assert.Contains("shipments.UpdateAsync(shipment, cancellationToken)", shipmentService, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentDispatch_ShouldReloadOrderRowVersionAfterAggregateLock()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var start = source.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", StringComparison.Ordinal);
        var end = source.IndexOf("else\n        {", start, StringComparison.Ordinal);
        var dispatch = source[start..end];
        var lockUpdate = dispatch.IndexOf("orders.UpdateAsync(order, cancellationToken)", StringComparison.Ordinal);
        var reload = dispatch.IndexOf("orders.GetByIdAsync(order.BasicId, cancellationToken)", StringComparison.Ordinal);
        var lineMutation = dispatch.IndexOf("orderLine.ReservedQuantity -= shipmentLine.Quantity", StringComparison.Ordinal);
        var finalUpdate = dispatch.LastIndexOf("orders.UpdateAsync(order, cancellationToken)", StringComparison.Ordinal);
        Assert.True(lockUpdate >= 0 && reload > lockUpdate && lineMutation > reload && finalUpdate > lineMutation);
        Assert.Contains("row-version token after acquiring the aggregate lock", dispatch, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftUpdate_ShouldReloadOrderRowVersionAfterAggregateLock()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var start = source.IndexOf("UpdateDraftAsync(CrmSalesOrderUpdateDto", StringComparison.Ordinal);
        var end = source.IndexOf("private static SysCrmSalesOrderLine CalculateLine", start, StringComparison.Ordinal);
        var update = source[start..end];
        var lockUpdate = update.IndexOf("repository.UpdateAsync(order, cancellationToken)", StringComparison.Ordinal);
        var reload = update.IndexOf("repository.GetByIdAsync(order.BasicId, cancellationToken)", StringComparison.Ordinal);
        var lineMutation = update.IndexOf("var existingLines =", StringComparison.Ordinal);
        var finalUpdate = update.LastIndexOf("repository.UpdateAsync(order, cancellationToken)", StringComparison.Ordinal);
        Assert.True(lockUpdate >= 0 && reload > lockUpdate && lineMutation > reload && finalUpdate > lineMutation);
        Assert.Contains("row-version token before the final order-header update", update, StringComparison.Ordinal);
    }


    [Fact]
    public void SalesOrderCreation_ShouldSupportOptionalIdempotentServerNumbering()
    {
        var dto = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmSalesOrderCreateDto.cs");
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        Assert.Contains("NumberingIdempotencyKey", dto, StringComparison.Ordinal);
        Assert.Contains("INumberGenerator numberGenerator", service, StringComparison.Ordinal);
        Assert.Contains("CrmNumberingRuleCodes.SalesOrder", service, StringComparison.Ordinal);
        Assert.Contains("NumberingScope.Auto", service, StringComparison.Ordinal);
        Assert.Contains("\"CRM_SALES_ORDER\"", service, StringComparison.Ordinal);
        Assert.Contains("NumberingScope.Auto", service, StringComparison.Ordinal);
        Assert.Contains("input.NumberingIdempotencyKey", service, StringComparison.Ordinal);
        Assert.Contains("allocation.Numbers.Single()", service, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectSalesOrderCreation_ShouldReplayMatchingRequestBeforeCreatingHeaderOrLines()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var createStart = source.IndexOf("CreateAsync(CrmSalesOrderCreateDto", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("UpdateDraftAsync(CrmSalesOrderUpdateDto", createStart, StringComparison.Ordinal);
        var create = source[createStart..nextMethod];

        Assert.Contains("CreateRequestFingerprint", create, StringComparison.Ordinal);
        Assert.Contains("GetByOrderNumberAsync(number, cancellationToken)", create, StringComparison.Ordinal);
        Assert.Contains("existingOrder.CreationRequestFingerprint", create, StringComparison.Ordinal);
        Assert.Contains("return ToListItem(existingOrder)", create, StringComparison.Ordinal);
        Assert.Contains("different or unverifiable create request", create, StringComparison.Ordinal);
        Assert.True(create.IndexOf("GetByOrderNumberAsync", StringComparison.Ordinal)
            < create.IndexOf("repository.AddAsync(order", StringComparison.Ordinal));
        Assert.True(create.IndexOf("GetByOrderNumberAsync", StringComparison.Ordinal)
            < create.IndexOf("orderLines.AddAsync(line", StringComparison.Ordinal));
    }

    [Fact]
    public void DirectSalesOrderCreation_ShouldFingerprintNormalizedCommercialPayload()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmSalesOrder.cs");
        Assert.Contains("SHA256.HashData", source, StringComparison.Ordinal);
        Assert.Contains("line.LineTotal", source, StringComparison.Ordinal);
        Assert.Contains("input.CustomerId", source, StringComparison.Ordinal);
        Assert.Contains("input.ContactId", source, StringComparison.Ordinal);
        Assert.Contains("input.OpportunityId", source, StringComparison.Ordinal);
        Assert.Contains("OwnerUserId = ownerUserId", source, StringComparison.Ordinal);
        Assert.Contains("CreationRequestFingerprint = creationRequestFingerprint", source, StringComparison.Ordinal);
        Assert.Contains("Creation_Request_Fingerprint", entity, StringComparison.Ordinal);
        Assert.Contains("Length = 64", entity, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesOrderCreateFingerprintMigration_ShouldAddNullableColumnForExistingRows()
    {
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.9", "5.9.9.sql");
        Assert.Contains("creation_request_fingerprint varchar(64) NULL", migration, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS", migration, StringComparison.Ordinal);
        Assert.Contains("information_schema.tables", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesOrderRepository_ShouldLookupByTenantScopedOrderNumber()
    {
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Repositories", "ICrmSalesOrderRepository.cs");
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmSalesOrderRepository.cs");
        Assert.Contains("GetByOrderNumberAsync", contract, StringComparison.Ordinal);
        Assert.Contains("CreateQueryable()", repository, StringComparison.Ordinal);
        Assert.Contains("order.OrderNumber == orderNumber", repository, StringComparison.Ordinal);
        Assert.Contains("ToListAsync(cancellationToken)", repository, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
