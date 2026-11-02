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
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Domain.Shared.Paging.Dtos;
using XiHan.Framework.Domain.Shared.Paging.Enums;
using XiHan.Framework.Domain.Shared.Paging.Models;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

/// <summary>Tenant-scoped lead queries with field-security filtering.</summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Leads")]
public sealed class CrmLeadQueryService(
    ICrmLeadRepository repository,
    IFieldSecurityService fieldSecurity)
    : CrmApplicationService, ICrmLeadQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    [HttpPost]
    public async Task<PageResultDtoBase<CrmLeadListItemDto>> GetPageAsync(CrmLeadPageQueryDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var request = new BasicAppPRDto
        {
            Page = input.Page,
            Conditions = new QueryConditions()
        };

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            request.Conditions.SetKeyword<SysCrmLead>(input.Keyword.Trim(),
                lead => lead.CompanyName, lead => lead.ContactName, lead => lead.Email);
        }

        if (input.Status.HasValue && Enum.IsDefined(typeof(CrmLeadStatus), input.Status.Value))
        {
            request.Conditions.AddFilter((SysCrmLead lead) => lead.Status, (CrmLeadStatus)input.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.CountryRegion))
        {
            request.Conditions.AddFilter((SysCrmLead lead) => lead.CountryRegion, input.CountryRegion.Trim());
        }

        if (input.Conditions?.Sorts is { Count: > 0 } sorts)
        {
            _ = request.Conditions.AddSorts(sorts);
        }

        if (input.Conditions?.Filters is { Count: > 0 } filters)
        {
            _ = request.Conditions.AddFilters(filters);
        }

        await fieldSecurity.GuardQueryAsync(request.Conditions, typeof(SysCrmLead), cancellationToken);

        if (request.Conditions.Sorts.Count == 0)
        {
            request.Conditions.AddSort((SysCrmLead lead) => lead.CreatedTime, SortDirection.Descending, 0);
        }

        var page = await repository.GetPagedAsync(request, cancellationToken);
        var items = page.Items.Select(CrmLeadMapper.ToListItemDto).ToList();
        return new PageResultDtoBase<CrmLeadListItemDto>(items, page.Page)
        {
            ExtendDatas = page.ExtendDatas
        };
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmLeadDetailDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Lead id must be greater than zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var entity = await repository.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        return CrmLeadMapper.ToDetailDto(entity);
    }
}
