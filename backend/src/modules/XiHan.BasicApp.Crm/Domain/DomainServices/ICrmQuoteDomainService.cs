using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.DomainServices;

public interface ICrmQuoteDomainService
{
    Task<SysCrmQuote> CreateAsync(CrmQuoteCreateCommand command, CancellationToken cancellationToken = default);
}

public sealed record CrmQuoteLineCommand(
    string? ProductCode,
    string Description,
    decimal Quantity,
    string? Unit,
    decimal UnitPrice,
    decimal DiscountRate,
    decimal TaxRate,
    int SortOrder);

public sealed record CrmQuoteCreateCommand(
    string QuoteNumber,
    long CustomerId,
    long? ContactId,
    long? OpportunityId,
    DateTime IssueDate,
    DateTime? ValidUntil,
    string Currency,
    long? OwnerUserId,
    string? TermsAndConditions,
    string? Notes,
    IReadOnlyList<CrmQuoteLineCommand> Lines);
