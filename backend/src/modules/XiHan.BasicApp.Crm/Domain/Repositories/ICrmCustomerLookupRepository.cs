namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Read-only tenant-scoped lookup used to validate CRM relationships during import preview.</summary>
public interface ICrmCustomerLookupRepository
{
    /// <summary>Returns the subset of supplied customer IDs that exist and are visible in the current tenant.</summary>
    Task<IReadOnlySet<long>> GetExistingIdsAsync(
        IReadOnlyCollection<long> customerIds,
        CancellationToken cancellationToken = default);
}
