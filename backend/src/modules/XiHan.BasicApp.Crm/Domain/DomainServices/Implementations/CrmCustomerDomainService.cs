using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;

public sealed class CrmCustomerDomainService(ICrmCustomerRepository repository) : ICrmCustomerDomainService
{
    public async Task<SysCrmCustomer> CreateAsync(CrmCustomerCreateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var entity = new SysCrmCustomer();
        Apply(entity, command.Name, command.LegalName, command.Website, command.CountryRegion, command.Industry, command.Source, command.OwnerUserId, command.Notes);
        return await repository.AddAsync(entity, cancellationToken);
    }

    public async Task<SysCrmCustomer> UpdateAsync(CrmCustomerUpdateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.Id <= 0) throw new ArgumentOutOfRangeException(nameof(command.Id));
        var entity = await repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException("Customer does not exist.");
        Apply(entity, command.Name, command.LegalName, command.Website, command.CountryRegion, command.Industry, command.Source, command.OwnerUserId, command.Notes);
        return await repository.UpdateAsync(entity, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var entity = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Customer does not exist.");
        if (!await repository.DeleteAsync(entity, cancellationToken))
            throw new InvalidOperationException("Customer deletion failed.");
    }

    private static void Apply(SysCrmCustomer entity, string name, string? legalName, string? website, string? country, string? industry, string? source, long? ownerId, string? notes)
    {
        entity.Name = Required(name, 200, nameof(name));
        entity.LegalName = Optional(legalName, 250, nameof(legalName));
        entity.Website = Optional(website, 500, nameof(website));
        entity.CountryRegion = Optional(country, 100, nameof(country));
        entity.Industry = Optional(industry, 120, nameof(industry));
        entity.Source = Optional(source, 80, nameof(source));
        if (ownerId is <= 0) throw new ArgumentOutOfRangeException(nameof(ownerId));
        entity.OwnerUserId = ownerId;
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
