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

/// <summary>Tenant- and owner-scoped sales order list queries.</summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Sales Orders")]
public sealed class CrmSalesOrderQueryService(
    ICrmSalesOrderRepository repository,
    IFieldSecurityService fieldSecurity,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmSalesOrderQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    [HttpPost]
    public async Task<PageResultDtoBase<CrmSalesOrderListItemDto>> GetPageAsync(CrmSalesOrderPageQueryDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new BasicAppPRDto { Page = input.Page, Conditions = new QueryConditions() };
        if (!string.IsNullOrWhiteSpace(input.Keyword))
            request.Conditions.SetKeyword<SysCrmSalesOrder>(input.Keyword.Trim(), x => x.OrderNumber, x => x.CustomerNameSnapshot, x => x.Currency);
        if (input.CustomerId.HasValue)
        {
            if (input.CustomerId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.CustomerId));
            request.Conditions.AddFilter((SysCrmSalesOrder x) => x.CustomerId, input.CustomerId.Value);
        }
        if (input.Status.HasValue)
        {
            if (!Enum.IsDefined(typeof(CrmSalesOrderStatus), input.Status.Value))
                throw new ArgumentOutOfRangeException(nameof(input.Status));
            request.Conditions.AddFilter((SysCrmSalesOrder x) => x.Status, (CrmSalesOrderStatus)input.Status.Value);
        }
        if (input.OwnerUserId.HasValue)
        {
            if (input.OwnerUserId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.OwnerUserId));
            request.Conditions.AddFilter((SysCrmSalesOrder x) => x.OwnerUserId, input.OwnerUserId.Value);
        }
        if (input.Conditions?.Sorts is { Count: > 0 } sorts) _ = request.Conditions.AddSorts(sorts);
        if (input.Conditions?.Filters is { Count: > 0 } filters) _ = request.Conditions.AddFilters(filters);
        await request.Conditions.ApplyOwnerScopeAsync(dataScope, (SysCrmSalesOrder x) => x.OwnerUserId, DateTimeOffset.UtcNow, cancellationToken);
        await fieldSecurity.GuardQueryAsync(request.Conditions, typeof(SysCrmSalesOrder), cancellationToken);
        if (request.Conditions.Sorts.Count == 0)
            request.Conditions.AddSort((SysCrmSalesOrder x) => x.CreatedTime, SortDirection.Descending, 0);

        var page = await repository.GetPagedAsync(request, cancellationToken);
        var items = page.Items.Select(x => new CrmSalesOrderListItemDto
        {
            BasicId = x.BasicId,
            OrderNumber = x.OrderNumber,
            SourceQuoteId = x.SourceQuoteId,
            CustomerId = x.CustomerId,
            CustomerNameSnapshot = x.CustomerNameSnapshot,
            Status = (int)x.Status,
            OrderDate = x.OrderDate,
            Currency = x.Currency,
            GrandTotal = x.GrandTotal,
            OwnerUserId = x.OwnerUserId,
            CreatedTime = x.CreatedTime
        }).ToList();
        return new PageResultDtoBase<CrmSalesOrderListItemDto>(items, page.Page)
        {
            ExtendDatas = page.ExtendDatas
        };
    }
}
