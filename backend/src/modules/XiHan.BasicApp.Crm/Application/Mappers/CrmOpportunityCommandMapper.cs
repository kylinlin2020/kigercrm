using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;

namespace XiHan.BasicApp.Crm.Application.Mappers;

public static class CrmOpportunityCommandMapper
{
    public static CrmOpportunityCreateCommand ToCommand(CrmOpportunityCreateDto input) =>
        new(input.CustomerId, input.PrimaryContactId, input.Name, input.Stage, input.Amount, input.Currency, input.ExpectedCloseDate, input.Probability, input.OwnerUserId, input.LostReason);

    public static CrmOpportunityUpdateCommand ToCommand(CrmOpportunityUpdateDto input) =>
        new(input.BasicId, input.CustomerId, input.PrimaryContactId, input.Name, input.Stage, input.Amount, input.Currency, input.ExpectedCloseDate, input.Probability, input.OwnerUserId, input.LostReason);
}
