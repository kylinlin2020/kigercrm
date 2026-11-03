using Microsoft.AspNetCore.Mvc;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.Extensions;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Domain.Shared.Paging.Dtos;
using XiHan.Framework.Domain.Shared.Paging.Enums;
using XiHan.Framework.Domain.Shared.Paging.Models;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Contacts")]
public sealed class CrmContactQueryService(
    ICrmContactRepository repository,
    IFieldSecurityService fieldSecurity,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmContactQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    [HttpPost]
    public async Task<PageResultDtoBase<CrmContactListItemDto>> GetPageAsync(CrmContactPageQueryDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new BasicAppPRDto { Page = input.Page, Conditions = new QueryConditions() };

        if (input.CustomerId.HasValue)
        {
            if (input.CustomerId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.CustomerId));
            request.Conditions.AddFilter((SysCrmContact x) => x.CustomerId, input.CustomerId.Value);
        }
        if (input.OwnerUserId.HasValue)
            request.Conditions.AddFilter((SysCrmContact x) => x.OwnerUserId, input.OwnerUserId.Value);
        if (!string.IsNullOrWhiteSpace(input.Keyword))
            request.Conditions.SetKeyword<SysCrmContact>(input.Keyword.Trim(), x => x.FirstName, x => x.LastName, x => x.Email, x => x.Phone);
        if (input.Conditions?.Sorts is { Count: > 0 } sorts) _ = request.Conditions.AddSorts(sorts);
        if (input.Conditions?.Filters is { Count: > 0 } filters) _ = request.Conditions.AddFilters(filters);

        await request.Conditions.ApplyOwnerScopeAsync(dataScope, (SysCrmContact x) => x.OwnerUserId, DateTimeOffset.UtcNow, cancellationToken);
        await fieldSecurity.GuardQueryAsync(request.Conditions, typeof(SysCrmContact), cancellationToken);
        if (request.Conditions.Sorts.Count == 0)
            request.Conditions.AddSort((SysCrmContact x) => x.CreatedTime, SortDirection.Descending, 0);

        var page = await repository.GetPagedAsync(request, cancellationToken);
        return new PageResultDtoBase<CrmContactListItemDto>(page.Items.Select(CrmAccountQueryMapper.ToContactListItem).ToList(), page.Page)
        { ExtendDatas = page.ExtendDatas };
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmContactDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await repository.GetByIdAsync(id, cancellationToken);
        if (entity is not null)
        {
            var scope = await dataScope.ResolveAccessibleUsersAsync(DateTimeOffset.UtcNow, cancellationToken);
            if (!scope.CanReadOwnedRecord(entity.OwnerUserId)) return null;
        }
        return entity is null ? null : CrmCoreMapper.ToDto(entity);
    }
}
