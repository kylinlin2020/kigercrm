using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Contacts")]
public sealed class CrmContactAppService(ICrmContactDomainService domainService, ICrmContactRepository records, ICrmCustomerRepository customers, IFieldSecurityService fieldSecurity, IUserDataScopeFilterService dataScope, IOperationPermissionGuard permissionGuard)
    : CrmApplicationService, ICrmContactAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmContactDto> CreateAsync(CrmContactCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        await EnsureCustomerAccessibleAsync(input.CustomerId, cancellationToken);
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
        var existing = await records.GetByIdAsync(input.BasicId, cancellationToken)
            ?? throw new InvalidOperationException("CRM record does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, existing.OwnerUserId, cancellationToken);
        await CrmAssignmentAuthorization.EnsureTransferPermissionAsync(permissionGuard, existing.OwnerUserId, input.OwnerUserId, cancellationToken);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        await EnsureCustomerAccessibleAsync(input.CustomerId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmContact), input.BasicId, input, cancellationToken);
        var entity = await domainService.UpdateAsync(CrmContactCommandMapper.ToCommand(input), cancellationToken);
        return CrmCoreMapper.ToDto(entity);
    }

    private async Task EnsureCustomerAccessibleAsync(long customerId, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer does not exist in the current tenant.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, customer.OwnerUserId, cancellationToken);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Delete)]
    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await records.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("CRM record does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, existing.OwnerUserId, cancellationToken);
        await domainService.DeleteAsync(id, cancellationToken);
    }
}
