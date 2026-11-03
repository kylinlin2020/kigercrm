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

[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Contacts")]
public sealed class CrmContactAppService(ICrmContactDomainService domainService, IFieldSecurityService fieldSecurity, IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmContactAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmContactDto> CreateAsync(CrmContactCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmContact), input, cancellationToken);
        var entity = await domainService.CreateAsync(CrmContactCommandMapper.ToCommand(input), cancellationToken);
        return CrmCoreMapper.ToDto(entity);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmContactDto> UpdateAsync(CrmContactUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmContact), input.BasicId, input, cancellationToken);
        var entity = await domainService.UpdateAsync(CrmContactCommandMapper.ToCommand(input), cancellationToken);
        return CrmCoreMapper.ToDto(entity);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Delete)]
    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => domainService.DeleteAsync(id, cancellationToken);
}
