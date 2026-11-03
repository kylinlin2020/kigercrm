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
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Activities")]
public sealed class CrmActivityAppService(ICrmActivityDomainService domainService, IFieldSecurityService fieldSecurity, IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmActivityAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmActivityDto> CreateAsync(CrmActivityCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmActivity), input, cancellationToken);
        return CrmCoreMapper.ToDto(await domainService.CreateAsync(CrmActivityCommandMapper.ToCommand(input), cancellationToken));
    }
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmActivityDto> UpdateAsync(CrmActivityUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmActivity), input.BasicId, input, cancellationToken);
        return CrmCoreMapper.ToDto(await domainService.UpdateAsync(input.BasicId, CrmActivityCommandMapper.ToCommand(input), cancellationToken));
    }
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Delete)]
    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => domainService.DeleteAsync(id, cancellationToken);
}