using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Quotes")]
public sealed class CrmQuoteAppService(
    ICrmQuoteDomainService domainService,
    ICrmQuoteRepository records,
    IFieldSecurityService fieldSecurity,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmQuoteAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmQuoteDto> CreateAsync(CrmQuoteCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmQuote), input, cancellationToken);
        if (input.Lines is null) throw new ArgumentException("Quotation lines are required.", nameof(input.Lines));
        foreach (var line in input.Lines)
            await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmQuoteLine), line, cancellationToken);
        var quote = await domainService.CreateAsync(CrmQuoteCommandMapper.ToCommand(input), cancellationToken);
        return new CrmQuoteDto
        {
            BasicId = quote.BasicId,
            QuoteNumber = quote.QuoteNumber,
            Version = quote.Version,
            CustomerId = quote.CustomerId,
            ContactId = quote.ContactId,
            OpportunityId = quote.OpportunityId,
            CustomerNameSnapshot = quote.CustomerNameSnapshot,
            ContactNameSnapshot = quote.ContactNameSnapshot,
            Status = (int)quote.Status,
            IssueDate = quote.IssueDate,
            ValidUntil = quote.ValidUntil,
            Currency = quote.Currency,
            Subtotal = quote.Subtotal,
            DiscountTotal = quote.DiscountTotal,
            TaxTotal = quote.TaxTotal,
            GrandTotal = quote.GrandTotal,
            OwnerUserId = quote.OwnerUserId
        };
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmQuoteDto> ChangeStatusAsync(CrmQuoteStatusUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Id <= 0) throw new ArgumentOutOfRangeException(nameof(input.Id));
        var existing = await records.GetByIdAsync(input.Id, cancellationToken)
            ?? throw new InvalidOperationException("CRM quote does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, existing.OwnerUserId, cancellationToken);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmQuote), input.Id, input, cancellationToken);
        return ToDto(await domainService.ChangeStatusAsync(input.Id, input.Status, cancellationToken));
    }

    private static CrmQuoteDto ToDto(SysCrmQuote quote) => new()
    {
        BasicId = quote.BasicId,
        QuoteNumber = quote.QuoteNumber,
        Version = quote.Version,
        CustomerId = quote.CustomerId,
        ContactId = quote.ContactId,
        OpportunityId = quote.OpportunityId,
        CustomerNameSnapshot = quote.CustomerNameSnapshot,
        ContactNameSnapshot = quote.ContactNameSnapshot,
        Status = (int)quote.Status,
        IssueDate = quote.IssueDate,
        ValidUntil = quote.ValidUntil,
        Currency = quote.Currency,
        Subtotal = quote.Subtotal,
        DiscountTotal = quote.DiscountTotal,
        TaxTotal = quote.TaxTotal,
        GrandTotal = quote.GrandTotal,
        OwnerUserId = quote.OwnerUserId
    };
}
