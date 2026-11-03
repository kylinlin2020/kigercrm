using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Leads")]
public sealed class CrmLeadAppService(ICrmLeadDomainService domainService, IFieldSecurityService fieldSecurity, IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmLeadAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmLeadDetailDto> CreateAsync(CrmLeadCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmLead), input, cancellationToken);
        var entity = await domainService.CreateAsync(CrmLeadCommandMapper.ToCommand(input), cancellationToken);
        return CrmLeadMapper.ToDetailDto(entity);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmLeadDetailDto> UpdateAsync(CrmLeadUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmLead), input.BasicId, input, cancellationToken);
        var entity = await domainService.UpdateAsync(CrmLeadCommandMapper.ToCommand(input), cancellationToken);
        return CrmLeadMapper.ToDetailDto(entity);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmLeadDetailDto> ConvertAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await domainService.ConvertAsync(id, cancellationToken);
        return CrmLeadMapper.ToDetailDto(entity);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Delete)]
    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => domainService.DeleteAsync(id, cancellationToken);
}
