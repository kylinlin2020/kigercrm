using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>Creates draft shipment manifests after validating the source order and its lines.</summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Shipments")]
public sealed class CrmShipmentAppService(
    ICrmShipmentRepository shipments,
    ICrmShipmentLineRepository shipmentLines,
    ICrmSalesOrderRepository orders,
    ICrmSalesOrderLineRepository orderLines,
    IFieldSecurityService fieldSecurity,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmShipmentAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmShipmentDto> CreateAsync(CrmShipmentCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input.SalesOrderId <= 0) throw new ArgumentOutOfRangeException(nameof(input.SalesOrderId));
        var number = NormalizeRequired(input.ShipmentNumber, 40, nameof(input.ShipmentNumber));
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
        if (order.Status is not (CrmSalesOrderStatus.Confirmed or CrmSalesOrderStatus.Processing or CrmSalesOrderStatus.PartiallyShipped))
            throw new InvalidOperationException("A shipment can only be prepared for a confirmed or in-progress sales order.");
        var owner = input.OwnerUserId ?? order.OwnerUserId;
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, owner, cancellationToken);

        var resolvedLines = new List<SysCrmSalesOrderLine>(input.Lines.Count);
        foreach (var requested in input.Lines)
        {
            var orderLine = await orderLines.GetByIdAsync(requested.SalesOrderLineId, cancellationToken)
                ?? throw new InvalidOperationException($"Sales-order line {requested.SalesOrderLineId} does not exist in the current tenant.");
            if (orderLine.SalesOrderId != order.BasicId)
                throw new InvalidOperationException("Every shipment line must belong to the selected sales order.");
            if (decimal.Round(requested.Quantity, 4, MidpointRounding.AwayFromZero) != requested.Quantity)
                throw new ArgumentOutOfRangeException(nameof(input.Lines), "Shipment quantities support at most four decimal places.");
            if (requested.Quantity > orderLine.Quantity)
                throw new InvalidOperationException("A shipment line quantity cannot exceed the ordered quantity.");
            resolvedLines.Add(orderLine);
        }

        var shipment = new SysCrmShipment
        {
            ShipmentNumber = number,
            SalesOrderId = order.BasicId,
            Status = CrmShipmentStatus.Draft,
            ShipmentDate = input.ShipmentDate,
            Carrier = NormalizeOptional(input.Carrier, 100, nameof(input.Carrier)),
            TrackingNumber = NormalizeOptional(input.TrackingNumber, 120, nameof(input.TrackingNumber)),
            OwnerUserId = owner,
            Notes = input.Notes
        };
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmShipment), input, cancellationToken);
        var saved = await shipments.AddAsync(shipment, cancellationToken);
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = new SysCrmShipmentLine
            {
                ShipmentId = saved.BasicId,
                SalesOrderLineId = resolvedLines[i].BasicId,
                Quantity = input.Lines[i].Quantity
            };
            await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmShipmentLine), line, cancellationToken);
            await shipmentLines.AddAsync(line, cancellationToken);
        }

        return new CrmShipmentDto
        {
            BasicId = saved.BasicId,
            ShipmentNumber = saved.ShipmentNumber,
            SalesOrderId = saved.SalesOrderId,
            Status = (int)saved.Status,
            ShipmentDate = saved.ShipmentDate,
            Carrier = saved.Carrier,
            TrackingNumber = saved.TrackingNumber,
            OwnerUserId = saved.OwnerUserId,
            CreatedTime = saved.CreatedTime
        };
    }

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
