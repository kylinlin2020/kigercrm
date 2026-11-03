using Microsoft.AspNetCore.Mvc;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
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
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Activities")]
public sealed class CrmActivityQueryService(ICrmActivityRepository repository, IFieldSecurityService fieldSecurity, IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmActivityQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    [HttpPost]
    public async Task<PageResultDtoBase<CrmActivityListItemDto>> GetPageAsync(CrmActivityPageQueryDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var request = new BasicAppPRDto { Page = input.Page, Conditions = new QueryConditions() };
        if (!string.IsNullOrWhiteSpace(input.RelatedEntityType))
        {
            var type = input.RelatedEntityType.Trim().ToLowerInvariant();
            if (type.Length > 40) throw new ArgumentOutOfRangeException(nameof(input.RelatedEntityType));
            request.Conditions.AddFilter((SysCrmActivity x) => x.RelatedEntityType, type);
        }
        if (input.RelatedEntityId.HasValue)
        {
            if (input.RelatedEntityId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.RelatedEntityId));
            request.Conditions.AddFilter((SysCrmActivity x) => x.RelatedEntityId, input.RelatedEntityId.Value);
        }
        if (input.ActivityType.HasValue)
        {
            if (!Enum.IsDefined(typeof(CrmActivityType), input.ActivityType.Value)) throw new ArgumentOutOfRangeException(nameof(input.ActivityType));
            request.Conditions.AddFilter((SysCrmActivity x) => x.ActivityType, (CrmActivityType)input.ActivityType.Value);
        }
        if (input.OwnerUserId.HasValue)
        {
            if (input.OwnerUserId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.OwnerUserId));
            request.Conditions.AddFilter((SysCrmActivity x) => x.OwnerUserId, input.OwnerUserId.Value);
        }
        if (input.OccurredFrom.HasValue) request.Conditions.AddFilter((SysCrmActivity x) => x.OccurredAt, SqlSugar.ConditionalType.GreaterThanOrEqual, input.OccurredFrom.Value);
        if (input.OccurredTo.HasValue) request.Conditions.AddFilter((SysCrmActivity x) => x.OccurredAt, SqlSugar.ConditionalType.LessThanOrEqual, input.OccurredTo.Value);
        if (input.OccurredFrom > input.OccurredTo && input.OccurredTo.HasValue) throw new ArgumentException("OccurredFrom must not be later than OccurredTo.");
        if (!string.IsNullOrWhiteSpace(input.Keyword)) request.Conditions.SetKeyword<SysCrmActivity>(input.Keyword.Trim(), x => x.Subject, x => x.Content, x => x.ExternalId);
        if (input.Conditions?.Sorts is { Count: > 0 } sorts) _ = request.Conditions.AddSorts(sorts);
        if (input.Conditions?.Filters is { Count: > 0 } filters) _ = request.Conditions.AddFilters(filters);
        await request.Conditions.ApplyOwnerScopeAsync(dataScope, (SysCrmActivity x) => x.OwnerUserId, DateTimeOffset.UtcNow, cancellationToken);
        await fieldSecurity.GuardQueryAsync(request.Conditions, typeof(SysCrmActivity), cancellationToken);
        if (request.Conditions.Sorts.Count == 0) request.Conditions.AddSort((SysCrmActivity x) => x.OccurredAt, SortDirection.Descending, 0);
        var page = await repository.GetPagedAsync(request, cancellationToken);
        var items = page.Items.Select(x => new CrmActivityListItemDto
        {
            BasicId = x.BasicId, RelatedEntityType = x.RelatedEntityType, RelatedEntityId = x.RelatedEntityId,
            ActivityType = (int)x.ActivityType, Subject = x.Subject, OccurredAt = x.OccurredAt, OwnerUserId = x.OwnerUserId
        }).ToList();
        return new PageResultDtoBase<CrmActivityListItemDto>(items, page.Page) { ExtendDatas = page.ExtendDatas };
    }
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmActivityDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var entity = await repository.GetByIdAsync(id, cancellationToken);
        if (entity is not null)
        {
            var scope = await dataScope.ResolveAccessibleUsersAsync(DateTimeOffset.UtcNow, cancellationToken);
            if (!scope.CanReadOwnedRecord(entity.OwnerUserId)) return null;
        }
        return entity is null ? null : CrmCoreMapper.ToDto(entity);
    }
}
