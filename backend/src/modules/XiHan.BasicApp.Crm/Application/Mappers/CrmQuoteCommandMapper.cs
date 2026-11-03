using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;

namespace XiHan.BasicApp.Crm.Application.Mappers;

public static class CrmQuoteCommandMapper
{
    public static CrmQuoteCreateCommand ToCommand(CrmQuoteCreateDto input) =>
        new(
            input.QuoteNumber,
            input.CustomerId,
            input.ContactId,
            input.OpportunityId,
            input.IssueDate,
            input.ValidUntil,
            input.Currency,
            input.OwnerUserId,
            input.TermsAndConditions,
            input.Notes,
            input.Lines.Select(x => new CrmQuoteLineCommand(
                x.ProductCode,
                x.Description,
                x.Quantity,
                x.Unit,
                x.UnitPrice,
                x.DiscountRate,
                x.TaxRate,
                x.SortOrder)).ToList());
}
