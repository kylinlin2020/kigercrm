using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;

public sealed class CrmOpportunityDomainService(
    ICrmOpportunityRepository opportunities,
    ICrmCustomerRepository customers,
    ICrmContactRepository contacts) : ICrmOpportunityDomainService
{
    public async Task<SysCrmOpportunity> CreateAsync(CrmOpportunityCreateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await EnsureReferencesAsync(command.CustomerId, command.PrimaryContactId, cancellationToken);
        var entity = new SysCrmOpportunity();
        Apply(entity, command.Name, command.Stage, command.Amount, command.Currency, command.ExpectedCloseDate, command.Probability, command.OwnerUserId, command.LostReason);
        entity.CustomerId = command.CustomerId;
        entity.PrimaryContactId = command.PrimaryContactId;
        return await opportunities.AddAsync(entity, cancellationToken);
    }

    public async Task<SysCrmOpportunity> UpdateAsync(CrmOpportunityUpdateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Id <= 0) throw new ArgumentOutOfRangeException(nameof(command.Id));
        await EnsureReferencesAsync(command.CustomerId, command.PrimaryContactId, cancellationToken);
        var entity = await opportunities.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException("Opportunity does not exist.");
        Apply(entity, command.Name, command.Stage, command.Amount, command.Currency, command.ExpectedCloseDate, command.Probability, command.OwnerUserId, command.LostReason);
        entity.CustomerId = command.CustomerId;
        entity.PrimaryContactId = command.PrimaryContactId;
        return await opportunities.UpdateAsync(entity, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var entity = await opportunities.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Opportunity does not exist.");
        if (!await opportunities.DeleteAsync(entity, cancellationToken))
            throw new InvalidOperationException("Opportunity deletion failed.");
    }

    private async Task EnsureReferencesAsync(long customerId, long? contactId, CancellationToken cancellationToken)
    {
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        _ = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer does not exist in the current tenant.");
        if (contactId is > 0)
        {
            var contact = await contacts.GetByIdAsync(contactId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Primary contact does not exist in the current tenant.");
            if (contact.CustomerId != customerId)
                throw new InvalidOperationException("Primary contact must belong to the selected customer.");
        }
        else if (contactId is not null)
        {
            throw new ArgumentOutOfRangeException(nameof(contactId));
        }
    }

    private static void Apply(SysCrmOpportunity entity, string name, int stage, decimal amount, string currency, DateTime? expectedCloseDate, int? probability, long? ownerId, string? lostReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalizedName = name.Trim();
        if (normalizedName.Length > 200) throw new ArgumentOutOfRangeException(nameof(name));
        if (!Enum.IsDefined(typeof(CrmOpportunityStage), stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 || normalizedCurrency.Any(c => c < 'A' || c > 'Z'))
            throw new ArgumentException("Currency must be a three-letter ISO-style code.", nameof(currency));
        if (probability is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(probability));
        if (ownerId is <= 0) throw new ArgumentOutOfRangeException(nameof(ownerId));
        if (lostReason?.Trim().Length > 500) throw new ArgumentOutOfRangeException(nameof(lostReason));
        var parsedStage = (CrmOpportunityStage)stage;
        if (parsedStage == CrmOpportunityStage.Lost && string.IsNullOrWhiteSpace(lostReason))
            throw new ArgumentException("Lost reason is required when an opportunity is Lost.", nameof(lostReason));
        entity.Name = normalizedName;
        entity.Stage = parsedStage;
        entity.Amount = amount;
        entity.Currency = normalizedCurrency;
        entity.ExpectedCloseDate = expectedCloseDate;
        entity.Probability = probability;
        entity.OwnerUserId = ownerId;
        entity.LostReason = string.IsNullOrWhiteSpace(lostReason) ? null : lostReason.Trim();
    }
}
