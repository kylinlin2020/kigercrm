using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Rehydrates provider requests exclusively from the immutable Run snapshot.
/// Both synchronous and durable asynchronous execution must use this factory so
/// retries never observe mutable Campaign/Strategy configuration.
/// </summary>
public static class CrmProspectingProviderRequestFactory
{
    public static CrmProspectingProviderRequest Create(
        SysCrmProspectingRun run,
        IReadOnlyList<string> excludedDomains)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(excludedDomains);

        if (run.BasicId <= 0)
            throw new ArgumentOutOfRangeException(nameof(run), "A persisted Run ID is required.");
        if (string.IsNullOrWhiteSpace(run.SnapshotSearchObjective))
            throw new ArgumentException("The immutable Run snapshot must contain a search objective.", nameof(run));

        var exclusions = excludedDomains
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(domain => domain, StringComparer.Ordinal)
            .ToArray();

        return new CrmProspectingProviderRequest(
            run.BasicId,
            run.SnapshotSearchObjective,
            run.SnapshotProductScope ?? string.Empty,
            run.SnapshotTargetMarkets ?? string.Empty,
            run.SnapshotIdealCustomerProfile ?? string.Empty,
            "{}",
            exclusions);
    }
}
