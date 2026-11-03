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
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Tasks")]
public sealed class CrmTaskAppService(ICrmTaskDomainService domainService, IFieldSecurityService fieldSecurity, IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmTaskAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmTaskDto> CreateAsync(CrmTaskCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.AssigneeUserId, cancellationToken);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.AssigneeUserId, cancellationToken);
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmTask), input, cancellationToken);
        return CrmCoreMapper.ToDto(await domainService.CreateAsync(CrmTaskCommandMapper.ToCommand(input), cancellationToken));
    }
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmTaskDto> UpdateAsync(CrmTaskUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmTask), input.BasicId, input, cancellationToken);
        return CrmCoreMapper.ToDto(await domainService.UpdateAsync(input.BasicId, CrmTaskCommandMapper.ToCommand(input), cancellationToken));
    }
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmTaskDto> ChangeStatusAsync(CrmTaskStatusUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmTask), input.Id, input, cancellationToken);
        return CrmCoreMapper.ToDto(await domainService.ChangeStatusAsync(input.Id, input.Status, cancellationToken));
    }
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Delete)]
    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => domainService.DeleteAsync(id, cancellationToken);
}