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
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Tasks")]
public sealed class CrmTaskQueryService(ICrmTaskRepository repository, IFieldSecurityService fieldSecurity, IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmTaskQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    [HttpPost]
    public async Task<PageResultDtoBase<CrmTaskListItemDto>> GetPageAsync(CrmTaskPageQueryDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var request = new BasicAppPRDto { Page = input.Page, Conditions = new QueryConditions() };
        if (!string.IsNullOrWhiteSpace(input.RelatedEntityType))
        {
            var type = input.RelatedEntityType.Trim().ToLowerInvariant();
            if (type.Length > 40) throw new ArgumentOutOfRangeException(nameof(input.RelatedEntityType));
            request.Conditions.AddFilter((SysCrmTask x) => x.RelatedEntityType, type);
        }
        if (input.RelatedEntityId.HasValue)
        {
            if (input.RelatedEntityId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.RelatedEntityId));
            request.Conditions.AddFilter((SysCrmTask x) => x.RelatedEntityId, input.RelatedEntityId.Value);
        }
        if (input.Status.HasValue)
        {
            if (!Enum.IsDefined(typeof(CrmTaskStatus), input.Status.Value)) throw new ArgumentOutOfRangeException(nameof(input.Status));
            request.Conditions.AddFilter((SysCrmTask x) => x.Status, (CrmTaskStatus)input.Status.Value);
        }
        if (input.Priority.HasValue)
        {
            if (input.Priority is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(input.Priority));
            request.Conditions.AddFilter((SysCrmTask x) => x.Priority, input.Priority.Value);
        }
        if (input.AssigneeUserId.HasValue)
        {
            if (input.AssigneeUserId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.AssigneeUserId));
            request.Conditions.AddFilter((SysCrmTask x) => x.AssigneeUserId, input.AssigneeUserId.Value);
        }
        if (input.DueFrom.HasValue) request.Conditions.AddFilter((SysCrmTask x) => x.DueAt, SqlSugar.ConditionalType.GreaterThanOrEqual, input.DueFrom.Value);
        if (input.DueTo.HasValue) request.Conditions.AddFilter((SysCrmTask x) => x.DueAt, SqlSugar.ConditionalType.LessThanOrEqual, input.DueTo.Value);
        if (input.DueFrom > input.DueTo && input.DueTo.HasValue) throw new ArgumentException("DueFrom must not be later than DueTo.");
        if (input.OverdueOnly == true)
        {
            request.Conditions.AddFilter((SysCrmTask x) => x.DueAt, SqlSugar.ConditionalType.LessThan, DateTime.UtcNow);
            request.Conditions.AddFilter((SysCrmTask x) => x.Status, SqlSugar.ConditionalType.NotEqual, CrmTaskStatus.Completed);
            request.Conditions.AddFilter((SysCrmTask x) => x.Status, SqlSugar.ConditionalType.NotEqual, CrmTaskStatus.Cancelled);
        }
        if (!string.IsNullOrWhiteSpace(input.Keyword)) request.Conditions.SetKeyword<SysCrmTask>(input.Keyword.Trim(), x => x.Title, x => x.Description);
        if (input.Conditions?.Sorts is { Count: > 0 } sorts) _ = request.Conditions.AddSorts(sorts);
        if (input.Conditions?.Filters is { Count: > 0 } filters) _ = request.Conditions.AddFilters(filters);
        await fieldSecurity.GuardQueryAsync(request.Conditions, typeof(SysCrmTask), cancellationToken);
        await request.Conditions.ApplyOwnerScopeAsync(dataScope, (SysCrmTask x) => x.AssigneeUserId, DateTimeOffset.UtcNow, cancellationToken);
        if (request.Conditions.Sorts.Count == 0) request.Conditions.AddSort((SysCrmTask x) => x.DueAt, SortDirection.Ascending, 0);
        var page = await repository.GetPagedAsync(request, cancellationToken);
        var items = page.Items.Select(x => new CrmTaskListItemDto
        {
            BasicId = x.BasicId, RelatedEntityType = x.RelatedEntityType, RelatedEntityId = x.RelatedEntityId,
            Title = x.Title, DueAt = x.DueAt, Status = (int)x.Status, Priority = x.Priority,
            AssigneeUserId = x.AssigneeUserId, CompletedAt = x.CompletedAt
        }).ToList();
        return new PageResultDtoBase<CrmTaskListItemDto>(items, page.Page) { ExtendDatas = page.ExtendDatas };
    }
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmTaskDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var entity = await repository.GetByIdAsync(id, cancellationToken);
        if (entity is not null)
        {
            var scope = await dataScope.ResolveAccessibleUsersAsync(DateTimeOffset.UtcNow, cancellationToken);
            if (!scope.CanReadOwnedRecord(entity.AssigneeUserId)) return null;
        }
        return entity is null ? null : CrmCoreMapper.ToDto(entity);
    }
}
