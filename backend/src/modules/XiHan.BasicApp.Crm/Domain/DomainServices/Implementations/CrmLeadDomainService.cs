using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;

public sealed class CrmLeadDomainService(ICrmLeadRepository repository) : ICrmLeadDomainService
{
    public async Task<SysCrmLead> CreateAsync(CrmLeadCreateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var entity = new SysCrmLead();
        Apply(entity, command.CompanyName, command.ContactName, command.Email, command.Phone, command.Website, command.CountryRegion, command.Source, command.SourceReference, command.OwnerUserId, command.Notes);
        entity.Status = CrmLeadStatus.New;
        entity.Score = 0;
        return await repository.AddAsync(entity, cancellationToken);
    }

    public async Task<SysCrmLead> UpdateAsync(CrmLeadUpdateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.Id <= 0) throw new ArgumentOutOfRangeException(nameof(command.Id));
        if (!Enum.IsDefined(typeof(CrmLeadStatus), command.Status) || command.Status == (int)CrmLeadStatus.Converted)
            throw new ArgumentOutOfRangeException(nameof(command.Status), "Lead status is invalid or requires the conversion workflow.");
        var entity = await repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException("Lead does not exist.");
        if (entity.Status == CrmLeadStatus.Converted)
            throw new InvalidOperationException("A converted lead cannot be edited.");
        Apply(entity, command.CompanyName, command.ContactName, command.Email, command.Phone, command.Website, command.CountryRegion, command.Source, command.SourceReference, command.OwnerUserId, command.Notes);
        entity.Status = (CrmLeadStatus)command.Status;
        return await repository.UpdateAsync(entity, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var entity = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Lead does not exist.");
        if (entity.Status == CrmLeadStatus.Converted)
            throw new InvalidOperationException("A converted lead cannot be deleted.");
        if (!await repository.DeleteAsync(entity, cancellationToken))
            throw new InvalidOperationException("Lead deletion failed.");
    }

    private static void Apply(SysCrmLead entity, string companyName, string? contactName, string? email, string? phone, string? website, string? country, string? source, string? sourceReference, long? ownerId, string? notes)
    {
        entity.CompanyName = Required(companyName, 200, nameof(companyName));
        entity.ContactName = Optional(contactName, 200, nameof(contactName));
        entity.Email = Optional(email, 254, nameof(email))?.ToLowerInvariant();
        entity.Phone = Optional(phone, 40, nameof(phone));
        entity.Website = Optional(website, 500, nameof(website));
        entity.CountryRegion = Optional(country, 100, nameof(country));
        entity.Source = Optional(source, 80, nameof(source));
        entity.SourceReference = Optional(sourceReference, 500, nameof(sourceReference));
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
