namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Builds an explicit provider capability map and rejects ambiguous provider identities.
/// A provider code may be registered exactly once across synchronous and asynchronous contracts.
/// </summary>
public sealed class CrmProspectingProviderRegistry
{
    private readonly IReadOnlyDictionary<string, ICrmProspectingProvider> _synchronous;
    private readonly IReadOnlyDictionary<string, ICrmProspectingAsyncExtractionProvider> _asynchronous;

    private CrmProspectingProviderRegistry(
        IReadOnlyDictionary<string, ICrmProspectingProvider> synchronous,
        IReadOnlyDictionary<string, ICrmProspectingAsyncExtractionProvider> asynchronous)
    {
        _synchronous = synchronous;
        _asynchronous = asynchronous;
    }

    public IReadOnlyDictionary<string, ICrmProspectingProvider> Synchronous => _synchronous;
    public IReadOnlyDictionary<string, ICrmProspectingAsyncExtractionProvider> Asynchronous => _asynchronous;

    public static CrmProspectingProviderRegistry Create(
        IEnumerable<ICrmProspectingProvider> synchronousProviders,
        IEnumerable<ICrmProspectingAsyncExtractionProvider> asynchronousProviders)
    {
        ArgumentNullException.ThrowIfNull(synchronousProviders);
        ArgumentNullException.ThrowIfNull(asynchronousProviders);

        var synchronous = new Dictionary<string, ICrmProspectingProvider>(StringComparer.OrdinalIgnoreCase);
        var asynchronous = new Dictionary<string, ICrmProspectingAsyncExtractionProvider>(StringComparer.OrdinalIgnoreCase);
        var allCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in synchronousProviders)
        {
            if (provider is null) throw new InvalidOperationException("A synchronous prospecting provider registration is null.");
            var code = NormalizeCode(provider.ProviderCode);
            if (!allCodes.Add(code))
                throw new InvalidOperationException($"Duplicate prospecting provider code registration: {code}.");
            synchronous.Add(code, provider);
        }

        foreach (var provider in asynchronousProviders)
        {
            if (provider is null) throw new InvalidOperationException("An asynchronous prospecting provider registration is null.");
            var code = NormalizeCode(provider.ProviderCode);
            if (!allCodes.Add(code))
                throw new InvalidOperationException($"Duplicate prospecting provider code registration: {code}.");
            asynchronous.Add(code, provider);
        }

        return new CrmProspectingProviderRegistry(synchronous, asynchronous);
    }

    private static string NormalizeCode(string? providerCode)
    {
        if (string.IsNullOrWhiteSpace(providerCode))
            throw new InvalidOperationException("A prospecting provider code cannot be blank.");
        var code = providerCode.Trim().ToLowerInvariant();
        if (code.Length > 80)
            throw new InvalidOperationException("A prospecting provider code cannot exceed 80 characters.");
        return code;
    }
}
