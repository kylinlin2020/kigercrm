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

/// <summary>Authorized sales order lifecycle commands.</summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Sales Orders")]
public sealed class CrmSalesOrderAppService(
    ICrmSalesOrderRepository repository,
    IFieldSecurityService fieldSecurity,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmSalesOrderAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmSalesOrderListItemDto> ChangeStatusAsync(CrmSalesOrderStatusUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Id <= 0) throw new ArgumentOutOfRangeException(nameof(input.Id));
        if (!Enum.IsDefined(typeof(CrmSalesOrderStatus), input.Status))
            throw new ArgumentOutOfRangeException(nameof(input.Status));

        var order = await repository.GetByIdAsync(input.Id, cancellationToken)
            ?? throw new InvalidOperationException("Sales order does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, order.OwnerUserId, cancellationToken);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmSalesOrder), input.Id, input, cancellationToken);

        var target = (CrmSalesOrderStatus)input.Status;
        var allowed = order.Status switch
        {
            CrmSalesOrderStatus.Draft => target is CrmSalesOrderStatus.Confirmed or CrmSalesOrderStatus.Cancelled,
            CrmSalesOrderStatus.Confirmed => target is CrmSalesOrderStatus.Processing or CrmSalesOrderStatus.Cancelled,
            // Fulfillment-derived states are written only by shipment workflows, never by manual status edits.
            CrmSalesOrderStatus.Processing => false,
            CrmSalesOrderStatus.PartiallyShipped => false,
            _ => false
        };
        if (!allowed)
            throw new InvalidOperationException($"Sales order status transition from {order.Status} to {target} is not allowed.");

        order.Status = target;
        var saved = await repository.UpdateAsync(order, cancellationToken);
        return new CrmSalesOrderListItemDto
        {
            BasicId = saved.BasicId,
            OrderNumber = saved.OrderNumber,
            SourceQuoteId = saved.SourceQuoteId,
            CustomerId = saved.CustomerId,
            CustomerNameSnapshot = saved.CustomerNameSnapshot,
            Status = (int)saved.Status,
            OrderDate = saved.OrderDate,
            Currency = saved.Currency,
            GrandTotal = saved.GrandTotal,
            OwnerUserId = saved.OwnerUserId,
            CreatedTime = saved.CreatedTime
        };
    }
}
