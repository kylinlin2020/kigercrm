using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Application.AppServices;

[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Customers")]
public sealed class CrmCustomerAppService(ICrmCustomerDomainService domainService, IFieldSecurityService fieldSecurity, IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmCustomerAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmCustomerDetailDto> CreateAsync(CrmCustomerCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmCustomer), input, cancellationToken);
        var entity = await domainService.CreateAsync(CrmCustomerCommandMapper.ToCommand(input), cancellationToken);
        return CrmAccountQueryMapper.ToCustomerDetail(entity);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmCustomerDetailDto> UpdateAsync(CrmCustomerUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmCustomer), input.BasicId, input, cancellationToken);
        var entity = await domainService.UpdateAsync(CrmCustomerCommandMapper.ToCommand(input), cancellationToken);
        return CrmAccountQueryMapper.ToCustomerDetail(entity);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Delete)]
    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => domainService.DeleteAsync(id, cancellationToken);
}
