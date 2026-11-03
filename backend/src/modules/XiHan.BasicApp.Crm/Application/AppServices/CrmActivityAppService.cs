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
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Activities")]
public sealed class CrmActivityAppService(ICrmActivityDomainService domainService, ICrmActivityRepository records, ICrmCustomerRepository customers, ICrmContactRepository contacts, ICrmLeadRepository leads, ICrmOpportunityRepository opportunities, IFieldSecurityService fieldSecurity, IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmActivityAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmActivityDto> CreateAsync(CrmActivityCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        await EnsureRelatedRecordAccessibleAsync(input.RelatedEntityType, input.RelatedEntityId, cancellationToken);
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmActivity), input, cancellationToken);
        return CrmCoreMapper.ToDto(await domainService.CreateAsync(CrmActivityCommandMapper.ToCommand(input), cancellationToken));
    }
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmActivityDto> UpdateAsync(CrmActivityUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var existing = await records.GetByIdAsync(input.BasicId, cancellationToken)
            ?? throw new InvalidOperationException("CRM record does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, existing.OwnerUserId, cancellationToken);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        await EnsureRelatedRecordAccessibleAsync(input.RelatedEntityType, input.RelatedEntityId, cancellationToken);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmActivity), input.BasicId, input, cancellationToken);
        return CrmCoreMapper.ToDto(await domainService.UpdateAsync(input.BasicId, CrmActivityCommandMapper.ToCommand(input), cancellationToken));
    }
    private async Task EnsureRelatedRecordAccessibleAsync(string relatedType, long relatedId, CancellationToken cancellationToken)
    {
        var type = relatedType.Trim().ToLowerInvariant();
        long? ownerUserId = type switch
        {
            "customer" => (await customers.GetByIdAsync(relatedId, cancellationToken))?.OwnerUserId
                ?? throw new InvalidOperationException("Related customer does not exist in the current tenant or has no accessible owner."),
            "contact" => (await contacts.GetByIdAsync(relatedId, cancellationToken))?.OwnerUserId
                ?? throw new InvalidOperationException("Related contact does not exist in the current tenant or has no accessible owner."),
            "lead" => (await leads.GetByIdAsync(relatedId, cancellationToken))?.OwnerUserId
                ?? throw new InvalidOperationException("Related lead does not exist in the current tenant or has no accessible owner."),
            "opportunity" => (await opportunities.GetByIdAsync(relatedId, cancellationToken))?.OwnerUserId
                ?? throw new InvalidOperationException("Related opportunity does not exist in the current tenant or has no accessible owner."),
            _ => throw new ArgumentException("Related entity type must be customer, contact, lead or opportunity.", nameof(relatedType))
        };
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, ownerUserId, cancellationToken);
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