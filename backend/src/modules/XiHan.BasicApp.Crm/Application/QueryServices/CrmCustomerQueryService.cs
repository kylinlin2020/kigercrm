using Microsoft.AspNetCore.Mvc;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.Extensions;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Domain.Shared.Paging.Dtos;
using XiHan.Framework.Domain.Shared.Paging.Enums;
using XiHan.Framework.Domain.Shared.Paging.Models;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Customers")]
public sealed class CrmCustomerQueryService(
    ICrmCustomerRepository repository,
    IFieldSecurityService fieldSecurity)
    : CrmApplicationService, ICrmCustomerQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    [HttpPost]
    public async Task<PageResultDtoBase<CrmCustomerListItemDto>> GetPageAsync(CrmCustomerPageQueryDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new BasicAppPRDto { Page = input.Page, Conditions = new QueryConditions() };

        if (!string.IsNullOrWhiteSpace(input.Keyword))
            request.Conditions.SetKeyword<SysCrmCustomer>(input.Keyword.Trim(), x => x.Name, x => x.LegalName, x => x.Website);
        if (!string.IsNullOrWhiteSpace(input.CountryRegion))
            request.Conditions.AddFilter((SysCrmCustomer x) => x.CountryRegion, input.CountryRegion.Trim());
        if (!string.IsNullOrWhiteSpace(input.Industry))
            request.Conditions.AddFilter((SysCrmCustomer x) => x.Industry, input.Industry.Trim());
        if (input.OwnerUserId.HasValue)
            request.Conditions.AddFilter((SysCrmCustomer x) => x.OwnerUserId, input.OwnerUserId.Value);
        if (input.Conditions?.Sorts is { Count: > 0 } sorts) _ = request.Conditions.AddSorts(sorts);
        if (input.Conditions?.Filters is { Count: > 0 } filters) _ = request.Conditions.AddFilters(filters);

        await fieldSecurity.GuardQueryAsync(request.Conditions, typeof(SysCrmCustomer), cancellationToken);
        if (request.Conditions.Sorts.Count == 0)
            request.Conditions.AddSort((SysCrmCustomer x) => x.CreatedTime, SortDirection.Descending, 0);

        var page = await repository.GetPagedAsync(request, cancellationToken);
        return new PageResultDtoBase<CrmCustomerListItemDto>(page.Items.Select(CrmAccountQueryMapper.ToCustomerListItem).ToList(), page.Page)
        { ExtendDatas = page.ExtendDatas };
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmCustomerDetailDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await repository.GetByIdAsync(id, cancellationToken);
        if (entity is null) return null;
        return CrmAccountQueryMapper.ToCustomerDetail(entity);
    }
}
