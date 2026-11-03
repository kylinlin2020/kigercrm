using Microsoft.AspNetCore.Mvc;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.Extensions;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Domain.Shared.Paging.Dtos;
using XiHan.Framework.Domain.Shared.Paging.Enums;
using XiHan.Framework.Domain.Shared.Paging.Models;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

/// <summary>Tenant- and owner-scoped shipment queries.</summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Shipments")]
public sealed class CrmShipmentQueryService(
    ICrmShipmentRepository repository,
    ICrmShipmentLineRepository shipmentLines,
    IFieldSecurityService fieldSecurity,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmShipmentQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    [HttpPost]
    public async Task<PageResultDtoBase<CrmShipmentListItemDto>> GetPageAsync(CrmShipmentPageQueryDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new BasicAppPRDto { Page = input.Page, Conditions = new QueryConditions() };
        if (!string.IsNullOrWhiteSpace(input.Keyword))
            request.Conditions.SetKeyword<SysCrmShipment>(input.Keyword.Trim(), x => x.ShipmentNumber, x => x.Carrier, x => x.TrackingNumber);
        if (input.SalesOrderId.HasValue)
        {
            if (input.SalesOrderId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.SalesOrderId));
            request.Conditions.AddFilter((SysCrmShipment x) => x.SalesOrderId, input.SalesOrderId.Value);
        }
        if (input.Status.HasValue)
        {
            if (!Enum.IsDefined(typeof(CrmShipmentStatus), input.Status.Value))
                throw new ArgumentOutOfRangeException(nameof(input.Status));
            request.Conditions.AddFilter((SysCrmShipment x) => x.Status, (CrmShipmentStatus)input.Status.Value);
        }
        if (input.OwnerUserId.HasValue)
        {
            if (input.OwnerUserId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.OwnerUserId));
            request.Conditions.AddFilter((SysCrmShipment x) => x.OwnerUserId, input.OwnerUserId.Value);
        }
        if (input.Conditions?.Sorts is { Count: > 0 } sorts) _ = request.Conditions.AddSorts(sorts);
        if (input.Conditions?.Filters is { Count: > 0 } filters) _ = request.Conditions.AddFilters(filters);
        await fieldSecurity.GuardQueryAsync(request.Conditions, typeof(SysCrmShipment), cancellationToken);
        await request.Conditions.ApplyOwnerScopeAsync(dataScope, (SysCrmShipment x) => x.OwnerUserId, DateTimeOffset.UtcNow, cancellationToken);
        if (request.Conditions.Sorts.Count == 0)
            request.Conditions.AddSort((SysCrmShipment x) => x.CreatedTime, SortDirection.Descending, 0);

        var page = await repository.GetPagedAsync(request, cancellationToken);
        var items = page.Items.Select(ToListItem).ToList();
        return new PageResultDtoBase<CrmShipmentListItemDto>(items, page.Page) { ExtendDatas = page.ExtendDatas };
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmShipmentDetailDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        cancellationToken.ThrowIfCancellationRequested();
        var shipment = await repository.GetByIdAsync(id, cancellationToken);
        if (shipment is null) return null;
        var scope = await dataScope.ResolveAccessibleUsersAsync(DateTimeOffset.UtcNow, cancellationToken);
        if (!scope.CanReadOwnedRecord(shipment.OwnerUserId)) return null;
        return new CrmShipmentDetailDto
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
            CreatedTime = shipment.CreatedTime,
            Notes = shipment.Notes
        };
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    [HttpPost]
    public async Task<PageResultDtoBase<CrmShipmentLineDto>> GetLinesPageAsync(CrmShipmentLinePageQueryDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ShipmentId <= 0) throw new ArgumentOutOfRangeException(nameof(input.ShipmentId));
        var shipment = await repository.GetByIdAsync(input.ShipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Shipment does not exist.");
        var scope = await dataScope.ResolveAccessibleUsersAsync(DateTimeOffset.UtcNow, cancellationToken);
        if (!scope.CanReadOwnedRecord(shipment.OwnerUserId))
            throw new UnauthorizedAccessException("Shipment is outside the current data scope.");

        var request = new BasicAppPRDto { Page = input.Page, Conditions = new QueryConditions() };
        request.Conditions.AddFilter((SysCrmShipmentLine x) => x.ShipmentId, input.ShipmentId);
        await fieldSecurity.GuardQueryAsync(request.Conditions, typeof(SysCrmShipmentLine), cancellationToken);
        var page = await shipmentLines.GetPagedAsync(request, cancellationToken);
        var items = page.Items.Select(x => new CrmShipmentLineDto
        {
            BasicId = x.BasicId,
            SalesOrderLineId = x.SalesOrderLineId,
            Quantity = x.Quantity
        }).ToList();
        return new PageResultDtoBase<CrmShipmentLineDto>(items, page.Page) { ExtendDatas = page.ExtendDatas };
    }

    private static CrmShipmentListItemDto ToListItem(SysCrmShipment shipment) => new()
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
}
