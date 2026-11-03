using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>Authorized sales order lifecycle commands.</summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Sales Orders")]
public sealed class CrmSalesOrderAppService(
    ICrmSalesOrderRepository repository,
    ICrmSalesOrderLineRepository orderLines,
    ICrmCustomerRepository customers,
    ICrmContactRepository contacts,
    ICrmOpportunityRepository opportunities,
    IFieldSecurityService fieldSecurity,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmSalesOrderAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmSalesOrderListItemDto> CreateAsync(CrmSalesOrderCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var number = NormalizeRequired(input.OrderNumber, 40, nameof(input.OrderNumber));
        var currency = NormalizeRequired(input.Currency, 3, nameof(input.Currency)).ToUpperInvariant();
        if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be a three-letter alphabetic code.", nameof(input.Currency));
        if (input.CustomerId <= 0) throw new ArgumentOutOfRangeException(nameof(input.CustomerId));
        if (input.Lines is null || input.Lines.Count == 0 || input.Lines.Count > 500)
            throw new ArgumentException("A sales order requires between 1 and 500 lines.", nameof(input.Lines));

        var customer = await customers.GetByIdAsync(input.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer does not exist in the current tenant.");
        var owner = input.OwnerUserId ?? customer.OwnerUserId;
        await CrmAssignmentAuthorization.EnsureAssignableAsync(dataScope, owner, cancellationToken);

        if (input.ContactId.HasValue)
        {
            var contact = await contacts.GetByIdAsync(input.ContactId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Contact does not exist in the current tenant.");
            if (contact.CustomerId != customer.BasicId)
                throw new InvalidOperationException("Contact must belong to the selected customer.");
        }
        if (input.OpportunityId.HasValue)
        {
            var opportunity = await opportunities.GetByIdAsync(input.OpportunityId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Opportunity does not exist in the current tenant.");
            if (opportunity.CustomerId != customer.BasicId)
                throw new InvalidOperationException("Opportunity must belong to the selected customer.");
        }

        var calculated = input.Lines.Select(CalculateLine).ToArray();
        var order = new SysCrmSalesOrder
        {
            OrderNumber = number,
            CustomerId = customer.BasicId,
            ContactId = input.ContactId,
            OpportunityId = input.OpportunityId,
            CustomerNameSnapshot = customer.Name,
            Status = CrmSalesOrderStatus.Draft,
            OrderDate = input.OrderDate == default ? DateTime.UtcNow : input.OrderDate,
            Currency = currency,
            Subtotal = calculated.Sum(x => x.LineSubtotal),
            DiscountTotal = calculated.Sum(x => x.LineDiscount),
            TaxTotal = calculated.Sum(x => x.LineTax),
            GrandTotal = calculated.Sum(x => x.LineTotal),
            OwnerUserId = owner,
            CustomerPurchaseOrderNumber = NormalizeOptional(input.CustomerPurchaseOrderNumber, 100, nameof(input.CustomerPurchaseOrderNumber)),
            PaymentTerms = NormalizeOptional(input.PaymentTerms, 500, nameof(input.PaymentTerms)),
            DeliveryTerms = NormalizeOptional(input.DeliveryTerms, 500, nameof(input.DeliveryTerms)),
            Notes = input.Notes
        };
        await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmSalesOrder), input, cancellationToken);
        var saved = await repository.AddAsync(order, cancellationToken);
        foreach (var line in calculated)
        {
            line.SalesOrderId = saved.BasicId;
            await fieldSecurity.EnsureCreatableAsync(typeof(SysCrmSalesOrderLine), line, cancellationToken);
            await orderLines.AddAsync(line, cancellationToken);
        }
        return ToListItem(saved);
    }

    private static SysCrmSalesOrderLine CalculateLine(CrmSalesOrderLineCreateDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Quantity <= 0 || input.UnitPrice < 0 || input.DiscountRate is < 0 or > 100 || input.TaxRate is < 0 or > 100 || input.SortOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(input), "Order line quantity, price, rates or sort order are invalid.");
        var quantity = decimal.Round(input.Quantity, 4, MidpointRounding.AwayFromZero);
        var unitPrice = decimal.Round(input.UnitPrice, 4, MidpointRounding.AwayFromZero);
        var discountRate = decimal.Round(input.DiscountRate, 4, MidpointRounding.AwayFromZero);
        var taxRate = decimal.Round(input.TaxRate, 4, MidpointRounding.AwayFromZero);
        var subtotal = decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
        var discount = decimal.Round(subtotal * discountRate / 100m, 2, MidpointRounding.AwayFromZero);
        var tax = decimal.Round((subtotal - discount) * taxRate / 100m, 2, MidpointRounding.AwayFromZero);
        return new SysCrmSalesOrderLine
        {
            ProductCode = NormalizeOptional(input.ProductCode, 100, nameof(input.ProductCode)),
            Description = NormalizeRequired(input.Description, 500, nameof(input.Description)),
            Quantity = quantity,
            Unit = NormalizeOptional(input.Unit, 30, nameof(input.Unit)),
            UnitPrice = unitPrice,
            DiscountRate = discountRate,
            TaxRate = taxRate,
            LineSubtotal = subtotal,
            LineDiscount = discount,
            LineTax = tax,
            LineTotal = subtotal - discount + tax,
            SortOrder = input.SortOrder
        };
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return NormalizeRequired(value, maxLength, parameterName);
    }

    private static CrmSalesOrderListItemDto ToListItem(SysCrmSalesOrder saved) => new()
    {
        BasicId = saved.BasicId,
        OrderNumber = saved.OrderNumber,
        SourceQuoteId = saved.SourceQuoteId,
        CustomerId = saved.CustomerId,
        CustomerNameSnapshot = saved.CustomerNameSnapshot,
        Status = (int)saved.Status,
        OrderDate = saved.OrderDate,
        Currency = saved.Currency,
        GrandTotal = saved.GrandTotal,
        OwnerUserId = saved.OwnerUserId,
        CreatedTime = saved.CreatedTime
    };

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmSalesOrderListItemDto> ChangeStatusAsync(CrmSalesOrderStatusUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Id <= 0) throw new ArgumentOutOfRangeException(nameof(input.Id));
        if (!Enum.IsDefined(typeof(CrmSalesOrderStatus), input.Status))
            throw new ArgumentOutOfRangeException(nameof(input.Status));

        var order = await repository.GetByIdAsync(input.Id, cancellationToken)
            ?? throw new InvalidOperationException("Sales order does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, order.OwnerUserId, cancellationToken);
        await fieldSecurity.EnsureUpdatableAsync(typeof(SysCrmSalesOrder), input.Id, input, cancellationToken);

        var target = (CrmSalesOrderStatus)input.Status;
        var allowed = order.Status switch
        {
            CrmSalesOrderStatus.Draft => target is CrmSalesOrderStatus.Confirmed or CrmSalesOrderStatus.Cancelled,
            CrmSalesOrderStatus.Confirmed => target is CrmSalesOrderStatus.Processing or CrmSalesOrderStatus.Cancelled,
            // Fulfillment-derived states are written only by shipment workflows, never by manual status edits.
            CrmSalesOrderStatus.Processing => false,
            CrmSalesOrderStatus.PartiallyShipped => false,
            _ => false
        };
        if (target == CrmSalesOrderStatus.Cancelled)
        {
            var lines = await orderLines.ListForOrderAsync(order.BasicId, cancellationToken);
            if (lines.Any(line => line.ReservedQuantity > 0 || line.ShippedQuantity > 0))
                throw new InvalidOperationException("Cancel all draft shipments before cancelling this sales order; reserved quantities must be released first.");
        }
        if (!allowed)
            throw new InvalidOperationException($"Sales order status transition from {order.Status} to {target} is not allowed.");

        order.Status = target;
        order.FulfillmentRevision++;
        var saved = await repository.UpdateAsync(order, cancellationToken);
        return new CrmSalesOrderListItemDto
        {
            BasicId = saved.BasicId,
            OrderNumber = saved.OrderNumber,
            SourceQuoteId = saved.SourceQuoteId,
            CustomerId = saved.CustomerId,
            CustomerNameSnapshot = saved.CustomerNameSnapshot,
            Status = (int)saved.Status,
            OrderDate = saved.OrderDate,
            Currency = saved.Currency,
            GrandTotal = saved.GrandTotal,
            OwnerUserId = saved.OwnerUserId,
            CreatedTime = saved.CreatedTime
        };
    }
}
