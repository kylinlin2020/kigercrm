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

/// <summary>Tenant-scoped opportunity pipeline queries.</summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Opportunities")]
public sealed class CrmOpportunityQueryService(
    ICrmOpportunityRepository repository,
    IFieldSecurityService fieldSecurity,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmOpportunityQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    [HttpPost]
    public async Task<PageResultDtoBase<CrmOpportunityListItemDto>> GetPageAsync(CrmOpportunityPageQueryDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var request = new BasicAppPRDto { Page = input.Page, Conditions = new QueryConditions() };
        if (input.CustomerId.HasValue)
        {
            if (input.CustomerId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.CustomerId));
            request.Conditions.AddFilter((SysCrmOpportunity x) => x.CustomerId, input.CustomerId.Value);
        }
        if (input.Stage.HasValue)
        {
            if (!Enum.IsDefined(typeof(CrmOpportunityStage), input.Stage.Value))
                throw new ArgumentOutOfRangeException(nameof(input.Stage));
            request.Conditions.AddFilter((SysCrmOpportunity x) => x.Stage, (CrmOpportunityStage)input.Stage.Value);
        }
        if (input.OwnerUserId.HasValue)
        {
            if (input.OwnerUserId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(input.OwnerUserId));
            request.Conditions.AddFilter((SysCrmOpportunity x) => x.OwnerUserId, input.OwnerUserId.Value);
        }
        if (!string.IsNullOrWhiteSpace(input.Keyword))
            request.Conditions.SetKeyword<SysCrmOpportunity>(input.Keyword.Trim(), x => x.Name, x => x.Currency, x => x.LostReason);
        if (input.Conditions?.Sorts is { Count: > 0 } sorts) _ = request.Conditions.AddSorts(sorts);
        if (input.Conditions?.Filters is { Count: > 0 } filters) _ = request.Conditions.AddFilters(filters);

        await request.Conditions.ApplyOwnerScopeAsync(dataScope, (SysCrmOpportunity x) => x.OwnerUserId, DateTimeOffset.UtcNow, cancellationToken);
        await fieldSecurity.GuardQueryAsync(request.Conditions, typeof(SysCrmOpportunity), cancellationToken);
        if (request.Conditions.Sorts.Count == 0)
            request.Conditions.AddSort((SysCrmOpportunity x) => x.CreatedTime, SortDirection.Descending, 0);

        var page = await repository.GetPagedAsync(request, cancellationToken);
        var items = page.Items.Select(x => new CrmOpportunityListItemDto
        {
            BasicId = x.BasicId,
            CustomerId = x.CustomerId,
            PrimaryContactId = x.PrimaryContactId,
            Name = x.Name,
            Stage = (int)x.Stage,
            Amount = x.Amount,
            Currency = x.Currency,
            ExpectedCloseDate = x.ExpectedCloseDate,
            Probability = x.Probability,
            OwnerUserId = x.OwnerUserId,
            CreatedTime = x.CreatedTime
        }).ToList();
        return new PageResultDtoBase<CrmOpportunityListItemDto>(items, page.Page)
        {
            ExtendDatas = page.ExtendDatas
        };
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmOpportunityDetailDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await repository.GetByIdAsync(id, cancellationToken);
        if (entity is not null)
        {
            var scope = await dataScope.ResolveAccessibleUsersAsync(DateTimeOffset.UtcNow, cancellationToken);
            if (!scope.CanReadOwnedRecord(entity.OwnerUserId)) return null;
        }
        if (entity is null) return null;
        var dto = CrmCoreMapper.ToDto(entity);
        return new CrmOpportunityDetailDto
        {
            BasicId = dto.BasicId,
            CustomerId = dto.CustomerId,
            PrimaryContactId = dto.PrimaryContactId,
            Name = dto.Name,
            Stage = dto.Stage,
            Amount = dto.Amount,
            Currency = dto.Currency,
            ExpectedCloseDate = dto.ExpectedCloseDate,
            Probability = dto.Probability,
            OwnerUserId = dto.OwnerUserId,
            LostReason = dto.LostReason,
            CreatedTime = entity.CreatedTime
        };
    }
}
