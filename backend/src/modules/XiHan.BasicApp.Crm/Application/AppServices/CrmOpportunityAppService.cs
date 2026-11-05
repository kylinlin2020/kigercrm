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

[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Opportunities")]
public sealed class CrmOpportunityAppService(ICrmOpportunityDomainService domainService, ICrmOpportunityRepository records, ICrmCustomerRepository customers, ICrmContactRepository contacts, IFieldSecurityService fieldSecurity, IUserDataScopeFilterService dataScope, IOperationPermissionGuard permissionGuard)
    : CrmApplicationService, ICrmOpportunityAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmOpportunityDto> CreateAsync(CrmOpportunityCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        await EnsureRelatedRecordsAccessibleAsync(input.CustomerId, input.PrimaryContactId, cancellationToken);
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmOpportunity), input, cancellationToken);
        return CrmCoreMapper.ToDto(await domainService.CreateAsync(CrmOpportunityCommandMapper.ToCommand(input), cancellationToken));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmOpportunityDto> UpdateAsync(CrmOpportunityUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var existing = await records.GetByIdAsync(input.BasicId, cancellationToken)
            ?? throw new InvalidOperationException("CRM record does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, existing.OwnerUserId, cancellationToken);
        await CrmAssignmentAuthorization.EnsureTransferPermissionAsync(permissionGuard, existing.OwnerUserId, input.OwnerUserId, cancellationToken);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        await EnsureRelatedRecordsAccessibleAsync(input.CustomerId, input.PrimaryContactId, cancellationToken);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmOpportunity), input.BasicId, input, cancellationToken);
        return CrmCoreMapper.ToDto(await domainService.UpdateAsync(CrmOpportunityCommandMapper.ToCommand(input), cancellationToken));
    }

    private async Task EnsureRelatedRecordsAccessibleAsync(long customerId, long? contactId, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer does not exist in the current tenant.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, customer.OwnerUserId, cancellationToken);
        if (contactId is > 0)
        {
            var contact = await contacts.GetByIdAsync(contactId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Primary contact does not exist in the current tenant.");
            await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, contact.OwnerUserId, cancellationToken);
        }
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
