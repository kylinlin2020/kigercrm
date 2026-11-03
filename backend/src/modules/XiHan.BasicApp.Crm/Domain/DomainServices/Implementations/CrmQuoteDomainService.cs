using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;

/// <summary>Creates validated draft quotations and calculates all commercial amounts on the server.</summary>
public sealed class CrmQuoteDomainService(
    ICrmQuoteRepository quotes,
    ICrmQuoteLineRepository quoteLines,
    ICrmCustomerRepository customers,
    ICrmContactRepository contacts,
    ICrmOpportunityRepository opportunities) : ICrmQuoteDomainService
{
    public async Task<SysCrmQuote> CreateAsync(CrmQuoteCreateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var quoteNumber = NormalizeRequired(command.QuoteNumber, 40, nameof(command.QuoteNumber));
        var currency = NormalizeCurrency(command.Currency);
        if (command.CustomerId <= 0) throw new ArgumentOutOfRangeException(nameof(command.CustomerId));
        if (command.OwnerUserId is <= 0) throw new ArgumentOutOfRangeException(nameof(command.OwnerUserId));
        if (command.IssueDate == default) throw new ArgumentOutOfRangeException(nameof(command.IssueDate));
        if (command.Lines is { Count: > 500 }) throw new ArgumentOutOfRangeException(nameof(command.Lines), "A quotation cannot contain more than 500 lines.");
        if (command.ValidUntil.HasValue && command.ValidUntil.Value.Date < command.IssueDate.Date)
            throw new ArgumentException("Quote validity date cannot precede its issue date.", nameof(command.ValidUntil));
        if (command.Lines is null || command.Lines.Count == 0)
            throw new ArgumentException("At least one quotation line is required.", nameof(command.Lines));

        var customer = await customers.GetByIdAsync(command.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer does not exist in the current tenant.");
        SysCrmContact? contact = null;
        if (command.ContactId.HasValue)
        {
            if (command.ContactId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(command.ContactId));
            contact = await contacts.GetByIdAsync(command.ContactId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Contact does not exist in the current tenant.");
            if (contact.CustomerId != customer.BasicId)
                throw new InvalidOperationException("Contact must belong to the selected customer.");
        }

        if (command.OpportunityId.HasValue)
        {
            if (command.OpportunityId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(command.OpportunityId));
            var opportunity = await opportunities.GetByIdAsync(command.OpportunityId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Opportunity does not exist in the current tenant.");
            if (opportunity.CustomerId != customer.BasicId)
                throw new InvalidOperationException("Opportunity must belong to the selected customer.");
        }

        var calculated = command.Lines.Select(CalculateLine).ToList();
        var subtotal = calculated.Sum(x => x.LineSubtotal);
        var discount = calculated.Sum(x => x.LineDiscount);
        var tax = calculated.Sum(x => x.LineTax);
        var total = calculated.Sum(x => x.LineTotal);

        var quote = await quotes.AddAsync(new SysCrmQuote
        {
            QuoteNumber = quoteNumber,
            Version = 1,
            CustomerId = customer.BasicId,
            ContactId = contact?.BasicId,
            OpportunityId = command.OpportunityId,
            CustomerNameSnapshot = customer.Name,
            ContactNameSnapshot = contact is null ? null : string.Join(" ", new[] { contact.FirstName, contact.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
            Status = CrmQuoteStatus.Draft,
            IssueDate = command.IssueDate,
            ValidUntil = command.ValidUntil,
            Currency = currency,
            Subtotal = subtotal,
            DiscountTotal = discount,
            TaxTotal = tax,
            GrandTotal = total,
            OwnerUserId = command.OwnerUserId,
            TermsAndConditions = command.TermsAndConditions,
            Notes = command.Notes
        }, cancellationToken);

        foreach (var line in calculated)
        {
            line.QuoteId = quote.BasicId;
            await quoteLines.AddAsync(line, cancellationToken);
        }
        return quote;
    }

    private static SysCrmQuoteLine CalculateLine(CrmQuoteLineCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var description = NormalizeRequired(input.Description, 500, nameof(input.Description));
        if (input.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(input.Quantity));
        if (input.UnitPrice < 0) throw new ArgumentOutOfRangeException(nameof(input.UnitPrice));
        if (input.DiscountRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(input.DiscountRate));
        if (input.TaxRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(input.TaxRate));
        if (input.SortOrder < 0) throw new ArgumentOutOfRangeException(nameof(input.SortOrder));
        var quantity = decimal.Round(input.Quantity, 4, MidpointRounding.AwayFromZero);
        var unitPrice = decimal.Round(input.UnitPrice, 4, MidpointRounding.AwayFromZero);
        var discountRate = decimal.Round(input.DiscountRate, 4, MidpointRounding.AwayFromZero);
        var taxRate = decimal.Round(input.TaxRate, 4, MidpointRounding.AwayFromZero);
        var subtotal = Money(quantity * unitPrice);
        var discount = Money(subtotal * discountRate / 100m);
        var taxable = subtotal - discount;
        var tax = Money(taxable * taxRate / 100m);
        return new SysCrmQuoteLine
        {
            ProductCode = string.IsNullOrWhiteSpace(input.ProductCode) ? null : NormalizeRequired(input.ProductCode, 100, nameof(input.ProductCode)),
            Description = description,
            Quantity = quantity,
            Unit = string.IsNullOrWhiteSpace(input.Unit) ? null : NormalizeRequired(input.Unit, 30, nameof(input.Unit)),
            UnitPrice = unitPrice,
            DiscountRate = discountRate,
            TaxRate = taxRate,
            LineSubtotal = subtotal,
            LineDiscount = discount,
            LineTax = tax,
            LineTotal = taxable + tax,
            SortOrder = input.SortOrder
        };
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string NormalizeCurrency(string value)
    {
        var currency = NormalizeRequired(value, 3, nameof(value)).ToUpperInvariant();
        if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be a three-letter alphabetic code.", nameof(value));
        return currency;
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }
}
