using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;

public sealed class CrmContactDomainService(ICrmContactRepository contacts, ICrmCustomerRepository customers) : ICrmContactDomainService
{
    public async Task<SysCrmContact> CreateAsync(CrmContactCreateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureCustomerAsync(command.CustomerId, cancellationToken);
        var entity = new SysCrmContact();
        Apply(entity, command.FirstName, command.LastName, command.JobTitle, command.Email, command.Phone, command.WhatsApp, command.OwnerUserId, command.IsPrimary, command.Notes);
        entity.CustomerId = command.CustomerId;
        entity.PrimaryCustomerId = command.IsPrimary ? command.CustomerId : null;
        return await contacts.AddAsync(entity, cancellationToken);
    }

    public async Task<SysCrmContact> UpdateAsync(CrmContactUpdateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.Id <= 0) throw new ArgumentOutOfRangeException(nameof(command.Id));
        await EnsureCustomerAsync(command.CustomerId, cancellationToken);
        var entity = await contacts.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException("Contact does not exist.");
        Apply(entity, command.FirstName, command.LastName, command.JobTitle, command.Email, command.Phone, command.WhatsApp, command.OwnerUserId, command.IsPrimary, command.Notes);
        entity.CustomerId = command.CustomerId;
        entity.PrimaryCustomerId = command.IsPrimary ? command.CustomerId : null;
        return await contacts.UpdateAsync(entity, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var entity = await contacts.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Contact does not exist.");
        if (!await contacts.DeleteAsync(entity, cancellationToken))
            throw new InvalidOperationException("Contact deletion failed.");
    }

    private async Task EnsureCustomerAsync(long customerId, CancellationToken cancellationToken)
    {
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        _ = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer does not exist in the current tenant.");
    }

    private static void Apply(SysCrmContact entity, string firstName, string lastName, string? jobTitle, string? email, string? phone, string? whatsApp, long? ownerId, bool isPrimary, string? notes)
    {
        entity.FirstName = Required(firstName, 100, nameof(firstName));
        entity.LastName = Required(lastName, 100, nameof(lastName));
        entity.JobTitle = Optional(jobTitle, 160, nameof(jobTitle));
        entity.Email = Optional(email, 254, nameof(email))?.ToLowerInvariant();
        entity.Phone = Optional(phone, 40, nameof(phone));
        entity.WhatsApp = Optional(whatsApp, 40, nameof(whatsApp));
        if (ownerId is <= 0) throw new ArgumentOutOfRangeException(nameof(ownerId));
        entity.OwnerUserId = ownerId;
        entity.IsPrimary = isPrimary;
        entity.Notes = notes?.Trim();
    }

    private static string Required(string? value, int max, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > max) throw new ArgumentOutOfRangeException(name, $"Maximum length is {max}.");
        return normalized;
    }

    private static string? Optional(string? value, int max, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > max) throw new ArgumentOutOfRangeException(name, $"Maximum length is {max}.");
        return normalized;
    }
}
