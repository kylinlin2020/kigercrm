using System.Security.Cryptography;
using System.Text.Json;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.Contracts;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Domain.Enums;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>Creates draft shipment manifests after validating the source order and its lines.</summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Shipments")]
public sealed class CrmShipmentAppService(
    ICrmShipmentRepository shipments,
    ICrmCreateRequestRepository createRequests,
    ICrmShipmentLineRepository shipmentLines,
    ICrmSalesOrderRepository orders,
    ICrmSalesOrderLineRepository orderLines,
    IFieldSecurityService fieldSecurity,
    IUserDataScopeFilterService dataScope,
    INumberGenerator numberGenerator)
    : CrmApplicationService, ICrmShipmentAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmShipmentDto> CreateAsync(CrmShipmentCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input.SalesOrderId <= 0) throw new ArgumentOutOfRangeException(nameof(input.SalesOrderId));
        var requestKey = string.IsNullOrWhiteSpace(input.NumberingIdempotencyKey)
            ? null
            : NormalizeRequired(input.NumberingIdempotencyKey, 128, nameof(input.NumberingIdempotencyKey));
        var number = requestKey is null
            ? NormalizeRequired(input.ShipmentNumber, 40, nameof(input.ShipmentNumber))
            : string.Empty;
        if (input.Lines is null || input.Lines.Count == 0)
            throw new ArgumentException("At least one shipment line is required.", nameof(input.Lines));
        if (input.Lines.Count > 500)
            throw new ArgumentOutOfRangeException(nameof(input.Lines), "A shipment cannot contain more than 500 lines.");
        if (input.Lines.Any(x => x is null || x.SalesOrderLineId <= 0 || x.Quantity <= 0))
            throw new ArgumentException("Each shipment line requires a valid sales-order line and a positive quantity.", nameof(input.Lines));
        if (input.Lines.Select(x => x.SalesOrderLineId).Distinct().Count() != input.Lines.Count)
            throw new ArgumentException("A sales-order line can appear only once in a shipment.", nameof(input.Lines));

        var order = await orders.GetByIdAsync(input.SalesOrderId, cancellationToken)
            ?? throw new InvalidOperationException("Sales order does not exist in the current tenant.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, order.OwnerUserId, cancellationToken);
        var owner = input.OwnerUserId ?? order.OwnerUserId;
        // Resolve the stable order of request lines before fingerprinting and locking.
        var requestedLines = input.Lines.OrderBy(line => line.SalesOrderLineId).ToArray();
        var carrier = NormalizeOptional(input.Carrier, 100, nameof(input.Carrier));
        var trackingNumber = NormalizeOptional(input.TrackingNumber, 120, nameof(input.TrackingNumber));
        string? creationRequestFingerprint = null;

        // The permanent request ledger is checked before order locks or quantity reservations.
        // A replay therefore cannot reserve the same sales-order quantity twice.
        if (requestKey is not null)
        {
            creationRequestFingerprint = CreateRequestFingerprint(input, owner, carrier, trackingNumber, requestedLines);
            var existingRequest = await createRequests.GetByRequestKeyAsync("SHIPMENT", requestKey, cancellationToken);
            if (existingRequest is not null)
            {
                if (!existingRequest.DocumentId.HasValue || string.IsNullOrWhiteSpace(existingRequest.DocumentNumber))
                    throw new InvalidOperationException("The shipment request ledger contains an incomplete committed record; manual repair is required.");

                var replayShipment = await shipments.GetByIdAsync(existingRequest.DocumentId.Value, cancellationToken)
                    ?? throw new InvalidOperationException("The shipment linked to this idempotency key no longer exists or is not visible.");
                await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, replayShipment.OwnerUserId, cancellationToken);
                if (!string.Equals(replayShipment.ShipmentNumber, existingRequest.DocumentNumber, StringComparison.Ordinal))
                    throw new InvalidOperationException("The shipment request ledger does not match its linked document.");

                // Owner defaults from the sales order; retain the persisted effective owner on replay.
                var replayFingerprint = CreateRequestFingerprint(
                    input, input.OwnerUserId ?? replayShipment.OwnerUserId, carrier, trackingNumber, requestedLines);
                if (!string.Equals(existingRequest.PayloadHash, replayFingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("The shipment idempotency key was already used with a different create request.");
                return ToDto(replayShipment);
            }

            var allocation = await numberGenerator.GenerateAsync(
                new NumberGenerateRequest(
                    CrmNumberingRuleCodes.Shipment,
                    NumberingScope.Auto,
                    requestKey,
                    "CRM_SHIPMENT"),
                cancellationToken);
            number = NormalizeRequired(allocation.Numbers.Single(), 40, nameof(input.NumberingIdempotencyKey));

            // Adopt a matching pre-ledger shipment created by the previous fingerprint-only implementation.
            var existingShipment = await shipments.GetByShipmentNumberAsync(number, cancellationToken);
            if (existingShipment is not null)
            {
                await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, existingShipment.OwnerUserId, cancellationToken);
                var legacyFingerprint = CreateRequestFingerprint(
                    input, input.OwnerUserId ?? existingShipment.OwnerUserId, carrier, trackingNumber, requestedLines);
                if (existingShipment.CreationRequestFingerprint is null ||
                    !string.Equals(existingShipment.CreationRequestFingerprint, legacyFingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("The shipment idempotency key was already used with a different or unverifiable create request.");

                await createRequests.AddAsync(new SysCrmCreateRequest
                {
                    DocumentType = "SHIPMENT",
                    RequestKey = requestKey,
                    PayloadHash = legacyFingerprint,
                    DocumentId = existingShipment.BasicId,
                    DocumentNumber = existingShipment.ShipmentNumber
                }, cancellationToken);
                return ToDto(existingShipment);
            }
        }

        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, owner, cancellationToken);

        if (order.Status is not (CrmSalesOrderStatus.Confirmed or CrmSalesOrderStatus.Processing or CrmSalesOrderStatus.PartiallyShipped))
            throw new InvalidOperationException("A shipment can only be prepared for a confirmed or in-progress sales order.");

        // Serialize fulfillment mutations at order level before reserving any line quantities.
        order.FulfillmentRevision++;
        order = await orders.UpdateAsync(order, cancellationToken);

        // Acquire order-line optimistic locks in a stable key order to reduce deadlocks
        // when concurrent multi-line shipments overlap on the same sales order.
        var resolvedLines = new List<SysCrmSalesOrderLine>(requestedLines.Length);
        foreach (var requested in requestedLines)
        {
            var orderLine = await orderLines.GetByIdAsync(requested.SalesOrderLineId, cancellationToken)
                ?? throw new InvalidOperationException($"Sales-order line {requested.SalesOrderLineId} does not exist in the current tenant.");
            if (orderLine.SalesOrderId != order.BasicId)
                throw new InvalidOperationException("Every shipment line must belong to the selected sales order.");
            if (decimal.Round(requested.Quantity, 4, MidpointRounding.AwayFromZero) != requested.Quantity)
                throw new ArgumentOutOfRangeException(nameof(input.Lines), "Shipment quantities support at most four decimal places.");
            var availableQuantity = orderLine.Quantity - orderLine.ReservedQuantity - orderLine.ShippedQuantity;
            if (requested.Quantity > availableQuantity)
                throw new InvalidOperationException("Shipment quantity exceeds the remaining unshipped and unreserved order quantity.");
            orderLine.ReservedQuantity += requested.Quantity;
            await orderLines.UpdateAsync(orderLine, cancellationToken);
            resolvedLines.Add(orderLine);
        }

        var shipment = new SysCrmShipment
        {
            ShipmentNumber = number,
            CreationRequestFingerprint = creationRequestFingerprint,
            SalesOrderId = order.BasicId,
            Status = CrmShipmentStatus.Draft,
            ShipmentDate = input.ShipmentDate,
            Carrier = carrier,
            TrackingNumber = trackingNumber,
            OwnerUserId = owner,
            Notes = input.Notes
        };
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmShipment), input, cancellationToken);
        var saved = await shipments.AddAsync(shipment, cancellationToken);
        for (var i = 0; i < requestedLines.Length; i++)
        {
            var line = new SysCrmShipmentLine
            {
                ShipmentId = saved.BasicId,
                SalesOrderLineId = resolvedLines[i].BasicId,
                Quantity = requestedLines[i].Quantity
            };
            await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmShipmentLine), line, cancellationToken);
            await shipmentLines.AddAsync(line, cancellationToken);
        }

        if (requestKey is not null && creationRequestFingerprint is not null)
        {
            await createRequests.AddAsync(new SysCrmCreateRequest
            {
                DocumentType = "SHIPMENT",
                RequestKey = requestKey,
                PayloadHash = creationRequestFingerprint,
                DocumentId = saved.BasicId,
                DocumentNumber = saved.ShipmentNumber
            }, cancellationToken);
        }

        return ToDto(saved);
    }


    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmShipmentDto> ChangeStatusAsync(CrmShipmentStatusUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Id <= 0) throw new ArgumentOutOfRangeException(nameof(input.Id));
        if (input.Status != (int)CrmShipmentStatus.Cancelled && input.Status != (int)CrmShipmentStatus.Dispatched && input.Status != (int)CrmShipmentStatus.Delivered)
            throw new ArgumentOutOfRangeException(nameof(input.Status));

        var shipment = await shipments.GetByIdAsync(input.Id, cancellationToken)
            ?? throw new InvalidOperationException("Shipment does not exist in the current tenant.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, shipment.OwnerUserId, cancellationToken);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmShipment), input.Id, input, cancellationToken);

        var target = (CrmShipmentStatus)input.Status;
        var lines = (await shipmentLines.GetByShipmentIdAsync(shipment.BasicId, cancellationToken))
            .OrderBy(line => line.SalesOrderLineId)
            .ToArray();
        if (lines.Length == 0)
            throw new InvalidOperationException("Shipment has no lines and cannot change fulfillment status.");

        if (target == CrmShipmentStatus.Cancelled)
        {
            if (shipment.Status != CrmShipmentStatus.Draft)
                throw new InvalidOperationException("Only a draft shipment can be cancelled.");

            // Keep the aggregate lock order consistent: order header first, then order lines.
            var order = await orders.GetByIdAsync(shipment.SalesOrderId, cancellationToken)
                ?? throw new InvalidOperationException("Source sales order no longer exists.");
            await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, order.OwnerUserId, cancellationToken);
            order.FulfillmentRevision++;
            await orders.UpdateAsync(order, cancellationToken);

            foreach (var shipmentLine in lines)
            {
                var orderLine = await orderLines.GetByIdAsync(shipmentLine.SalesOrderLineId, cancellationToken)
                    ?? throw new InvalidOperationException("A source sales-order line no longer exists.");
                if (orderLine.SalesOrderId != shipment.SalesOrderId || orderLine.ReservedQuantity < shipmentLine.Quantity)
                    throw new InvalidOperationException("Shipment reservation ledger is inconsistent; cancellation was stopped.");
                orderLine.ReservedQuantity -= shipmentLine.Quantity;
                await orderLines.UpdateAsync(orderLine, cancellationToken);
            }

        }
        else if (target == CrmShipmentStatus.Dispatched)
        {
            if (shipment.Status != CrmShipmentStatus.Draft)
                throw new InvalidOperationException("Only a draft shipment can be dispatched.");

            // Acquire the order aggregate's optimistic lock before any line-row locks.
            // This matches draft reservation and cancellation and prevents lock-order deadlocks.
            var order = await orders.GetByIdAsync(shipment.SalesOrderId, cancellationToken)
                ?? throw new InvalidOperationException("Source sales order no longer exists.");
            await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, order.OwnerUserId, cancellationToken);
            if (order.Status is not (CrmSalesOrderStatus.Confirmed or CrmSalesOrderStatus.Processing or CrmSalesOrderStatus.PartiallyShipped))
                throw new InvalidOperationException("The source sales order is not eligible for dispatch.");
            order.FulfillmentRevision++;
            order = await orders.UpdateAsync(order, cancellationToken);
            // Reload the row-version token after acquiring the aggregate lock before the final status update.
            order = await orders.GetByIdAsync(order.BasicId, cancellationToken)
                ?? throw new InvalidOperationException("Source sales order disappeared after its fulfillment lock was acquired.");

            foreach (var shipmentLine in lines)
            {
                var orderLine = await orderLines.GetByIdAsync(shipmentLine.SalesOrderLineId, cancellationToken)
                    ?? throw new InvalidOperationException("A source sales-order line no longer exists.");
                if (orderLine.SalesOrderId != shipment.SalesOrderId
                    || orderLine.ReservedQuantity < shipmentLine.Quantity
                    || orderLine.ShippedQuantity + shipmentLine.Quantity > orderLine.Quantity)
                    throw new InvalidOperationException("Shipment reservation ledger is inconsistent; dispatch was stopped.");
                orderLine.ReservedQuantity -= shipmentLine.Quantity;
                orderLine.ShippedQuantity += shipmentLine.Quantity;
                await orderLines.UpdateAsync(orderLine, cancellationToken);
            }

            var orderLineRows = await orderLines.ListForOrderAsync(order.BasicId, cancellationToken);
            if (orderLineRows.Count == 0)
                throw new InvalidOperationException("Source sales order has no lines.");
            order.Status = orderLineRows.All(x => x.ShippedQuantity >= x.Quantity)
                ? CrmSalesOrderStatus.Completed
                : CrmSalesOrderStatus.PartiallyShipped;
            order.FulfillmentRevision++;
            await orders.UpdateAsync(order, cancellationToken);
            shipment.ShipmentDate ??= DateTime.UtcNow;
        }
        else
        {
            if (shipment.Status != CrmShipmentStatus.Dispatched)
                throw new InvalidOperationException("Only a dispatched shipment can be marked delivered.");
            shipment.DeliveredDate ??= DateTime.UtcNow;
        }

        shipment.Status = target;
        var saved = await shipments.UpdateAsync(shipment, cancellationToken);
        return new CrmShipmentDto
        {
            BasicId = saved.BasicId,
            ShipmentNumber = saved.ShipmentNumber,
            SalesOrderId = saved.SalesOrderId,
            Status = (int)saved.Status,
            ShipmentDate = saved.ShipmentDate,
            DeliveredDate = saved.DeliveredDate,
            Carrier = saved.Carrier,
            TrackingNumber = saved.TrackingNumber,
            OwnerUserId = saved.OwnerUserId,
            CreatedTime = saved.CreatedTime
        };
    }

    private static string CreateRequestFingerprint(
        CrmShipmentCreateDto input,
        long? ownerUserId,
        string? carrier,
        string? trackingNumber,
        IReadOnlyList<CrmShipmentLineCreateDto> lines)
    {
        var canonicalRequest = new
        {
            input.SalesOrderId,
            input.ShipmentDate,
            Carrier = carrier,
            TrackingNumber = trackingNumber,
            OwnerUserId = ownerUserId,
            input.Notes,
            Lines = lines.Select(line => new { line.SalesOrderLineId, line.Quantity }).ToArray()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonicalRequest);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static CrmShipmentDto ToDto(SysCrmShipment shipment) => new()
    {
        BasicId = shipment.BasicId,
        ShipmentNumber = shipment.ShipmentNumber,
        SalesOrderId = shipment.SalesOrderId,
        Status = (int)shipment.Status,
        ShipmentDate = shipment.ShipmentDate,
        DeliveredDate = shipment.DeliveredDate,
        Carrier = shipment.Carrier,
        TrackingNumber = shipment.TrackingNumber,
        OwnerUserId = shipment.OwnerUserId,
        CreatedTime = shipment.CreatedTime
    };

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return NormalizeRequired(value, maxLength, parameterName);
    }
}
