using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.Contracts;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Domain.Enums;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Quotes")]
public sealed class CrmQuoteAppService(
    ICrmQuoteDomainService domainService,
    ICrmQuoteRepository records,
    ICrmQuoteLineRepository quoteLines,
    ICrmSalesOrderRepository salesOrders,
    ICrmSalesOrderLineRepository salesOrderLines,
    ICrmCustomerRepository customers,
    ICrmContactRepository contacts,
    ICrmOpportunityRepository opportunities,
    IFieldSecurityService fieldSecurity,
    IUserDataScopeFilterService dataScope,
    INumberGenerator numberGenerator)
    : CrmApplicationService, ICrmQuoteAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmQuoteDto> CreateAsync(CrmQuoteCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, input.OwnerUserId, cancellationToken);
        await EnsureRelatedRecordsAccessibleAsync(input.CustomerId, input.ContactId, input.OpportunityId, cancellationToken);
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmQuote), input, cancellationToken);
        if (input.Lines is null) throw new ArgumentException("Quotation lines are required.", nameof(input.Lines));
        foreach (var line in input.Lines)
            await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmQuoteLine), line, cancellationToken);
        var command = CrmQuoteCommandMapper.ToCommand(input);
        if (!string.IsNullOrWhiteSpace(input.NumberingIdempotencyKey))
        {
            var allocation = await numberGenerator.GenerateAsync(
                new NumberGenerateRequest(
                    CrmNumberingRuleCodes.Quote,
                    NumberingScope.Auto,
                    input.NumberingIdempotencyKey,
                    "CRM_QUOTE"),
                cancellationToken);
            command = command with { QuoteNumber = allocation.Numbers.Single() };
        }
        var quote = await domainService.CreateAsync(command, cancellationToken);
        return ToDto(quote);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmQuoteDto> ReviseAsync(long sourceQuoteId, CrmQuoteCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (sourceQuoteId <= 0) throw new ArgumentOutOfRangeException(nameof(sourceQuoteId));
        var source = await records.GetByIdAsync(sourceQuoteId, cancellationToken)
            ?? throw new InvalidOperationException("Source quote does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, source.OwnerUserId, cancellationToken);
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, source.OwnerUserId, cancellationToken);
        await EnsureRelatedRecordsAccessibleAsync(source.CustomerId, source.ContactId, source.OpportunityId, cancellationToken);
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmQuote), input, cancellationToken);
        if (input.Lines is null) throw new ArgumentException("Quotation lines are required.", nameof(input.Lines));
        foreach (var line in input.Lines)
            await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmQuoteLine), line, cancellationToken);
        return ToDto(await domainService.CreateRevisionAsync(sourceQuoteId, CrmQuoteCommandMapper.ToCommand(input), cancellationToken));
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

    /// <summary>
    /// Converts an accepted quotation into an immutable sales-order snapshot.
    /// The source-quote unique index is the final concurrent-request guard; the UnitOfWork
    /// rolls back order headers/lines if the quote optimistic update or uniqueness check fails.
    /// </summary>
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmSalesOrderListItemDto> ConvertToSalesOrderAsync(long quoteId, CancellationToken cancellationToken = default)
    {
        if (quoteId <= 0) throw new ArgumentOutOfRangeException(nameof(quoteId));
        cancellationToken.ThrowIfCancellationRequested();

        var quote = await records.GetByIdAsync(quoteId, cancellationToken)
            ?? throw new InvalidOperationException("CRM quote does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, quote.OwnerUserId, cancellationToken);
        await EnsureRelatedRecordsAccessibleAsync(quote.CustomerId, quote.ContactId, quote.OpportunityId, cancellationToken);
        if (quote.Status != CrmQuoteStatus.Accepted && quote.Status != CrmQuoteStatus.Converted)
            throw new InvalidOperationException("Only an accepted quote can be converted to a sales order.");
        if (quote.Status == CrmQuoteStatus.Converted && !quote.ConvertedOrderId.HasValue)
            throw new InvalidOperationException("The converted quote has no linked sales order; manual repair is required.");
        if (quote.Status == CrmQuoteStatus.Accepted && quote.ConvertedOrderId.HasValue)
            throw new InvalidOperationException("The accepted quote already has a converted-order link; manual repair is required.");

        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmQuote), quote.BasicId, quote, cancellationToken);

        // A retry after a lost HTTP response must return the order already committed for this quote.
        // The tenant-scoped unique source-quote index remains the concurrent first-request guard.
        if (quote.ConvertedOrderId.HasValue)
        {
            var existingOrder = await salesOrders.GetByIdAsync(quote.ConvertedOrderId.Value, cancellationToken)
                ?? throw new InvalidOperationException("The quote is marked converted, but its linked sales order does not exist.");
            if (existingOrder.SourceQuoteId != quote.BasicId)
                throw new InvalidOperationException("The quote's linked sales order has an inconsistent source-quote reference.");
            await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, existingOrder.OwnerUserId, cancellationToken);
            return new CrmSalesOrderListItemDto
            {
                BasicId = existingOrder.BasicId,
                OrderNumber = existingOrder.OrderNumber,
                SourceQuoteId = existingOrder.SourceQuoteId,
                CustomerId = existingOrder.CustomerId,
                CustomerNameSnapshot = existingOrder.CustomerNameSnapshot,
                Status = (int)existingOrder.Status,
                OrderDate = existingOrder.OrderDate,
                Currency = existingOrder.Currency,
                GrandTotal = existingOrder.GrandTotal,
                OwnerUserId = existingOrder.OwnerUserId,
                CreatedTime = existingOrder.CreatedTime
            };
        }

        var sourceLines = await quoteLines.ListForQuoteAsync(quote.BasicId, cancellationToken);
        if (sourceLines.Count == 0)
            throw new InvalidOperationException("A quotation without lines cannot be converted.");

        var order = new SysCrmSalesOrder
        {
            OrderNumber = $"SO-{quote.BasicId}",
            SourceQuoteId = quote.BasicId,
            CustomerId = quote.CustomerId,
            ContactId = quote.ContactId,
            OpportunityId = quote.OpportunityId,
            CustomerNameSnapshot = quote.CustomerNameSnapshot,
            Status = CrmSalesOrderStatus.Draft,
            OrderDate = DateTime.UtcNow,
            Currency = quote.Currency,
            Subtotal = quote.Subtotal,
            DiscountTotal = quote.DiscountTotal,
            TaxTotal = quote.TaxTotal,
            GrandTotal = quote.GrandTotal,
            OwnerUserId = quote.OwnerUserId,
            TermsAndConditions = quote.TermsAndConditions,
            Notes = quote.Notes
        };
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmSalesOrder), order, cancellationToken);
        var createdOrder = await salesOrders.AddAsync(order, cancellationToken);

        foreach (var sourceLine in sourceLines)
        {
            var orderLine = new SysCrmSalesOrderLine
            {
                SalesOrderId = createdOrder.BasicId,
                SourceQuoteLineId = sourceLine.BasicId,
                ProductCode = sourceLine.ProductCode,
                Description = sourceLine.Description,
                Quantity = sourceLine.Quantity,
                Unit = sourceLine.Unit,
                UnitPrice = sourceLine.UnitPrice,
                DiscountRate = sourceLine.DiscountRate,
                TaxRate = sourceLine.TaxRate,
                LineSubtotal = sourceLine.LineSubtotal,
                LineDiscount = sourceLine.LineDiscount,
                LineTax = sourceLine.LineTax,
                LineTotal = sourceLine.LineTotal,
                SortOrder = sourceLine.SortOrder
            };
            await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmSalesOrderLine), orderLine, cancellationToken);
            await salesOrderLines.AddAsync(orderLine, cancellationToken);
        }

        quote.Status = CrmQuoteStatus.Converted;
        quote.ConvertedOrderId = createdOrder.BasicId;
        await records.UpdateAsync(quote, cancellationToken);

        return new CrmSalesOrderListItemDto
        {
            BasicId = createdOrder.BasicId,
            OrderNumber = createdOrder.OrderNumber,
            SourceQuoteId = createdOrder.SourceQuoteId,
            CustomerId = createdOrder.CustomerId,
            CustomerNameSnapshot = createdOrder.CustomerNameSnapshot,
            Status = (int)createdOrder.Status,
            OrderDate = createdOrder.OrderDate,
            Currency = createdOrder.Currency,
            GrandTotal = createdOrder.GrandTotal,
            OwnerUserId = createdOrder.OwnerUserId,
            CreatedTime = createdOrder.CreatedTime
        };
    }

    private async Task EnsureRelatedRecordsAccessibleAsync(long customerId, long? contactId, long? opportunityId, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer does not exist in the current tenant.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, customer.OwnerUserId, cancellationToken);
        if (contactId is > 0)
        {
            var contact = await contacts.GetByIdAsync(contactId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Contact does not exist in the current tenant.");
            await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, contact.OwnerUserId, cancellationToken);
            if (contact.CustomerId != customer.BasicId)
                throw new InvalidOperationException("Contact must belong to the quoted customer.");
        }
        if (opportunityId is > 0)
        {
            var opportunity = await opportunities.GetByIdAsync(opportunityId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Opportunity does not exist in the current tenant.");
            await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, opportunity.OwnerUserId, cancellationToken);
            if (opportunity.CustomerId != customer.BasicId)
                throw new InvalidOperationException("Opportunity must belong to the quoted customer.");
        }
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
