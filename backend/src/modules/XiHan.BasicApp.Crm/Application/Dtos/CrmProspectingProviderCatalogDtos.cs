namespace XiHan.BasicApp.Crm.Application.Dtos;

/// <summary>
/// A catalog row combining static capability/licence metadata with runtime registration facts.
/// <c>IsRegistered</c> is derived from the DI provider registry, never from a configuration file guess.
/// </summary>
public sealed class CrmProspectingProviderCatalogItemDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public bool NeedsKey { get; set; }
    public bool KeyOptional { get; set; }
    public string ConfigKey { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    /// <summary>The adapter is registered in DI (server opt-in switch is on and construction succeeded).</summary>
    public bool IsRegistered { get; set; }

    /// <summary>The durable Run worker can execute it now: synchronous adapters only.</summary>
    public bool IsExecutable { get; set; }

    /// <summary>synchronous | asynchronous | unavailable.</summary>
    public string Capability { get; set; } = "unavailable";
}

/// <summary>Provider catalog response for the strategy editor and operations diagnostics.</summary>
public sealed class CrmProspectingProviderCatalogDto
{
    public IReadOnlyList<CrmProspectingProviderCatalogItemDto> Providers { get; set; } = [];
    public int TotalCount { get; set; }
    public int RegisteredCount { get; set; }
    public int ExecutableCount { get; set; }
    public int AsynchronousCount { get; set; }
    public string MetadataSource { get; set; } = "CrmProspectingProviderCatalog.All";
    public string RegistrationSource { get; set; } = "CrmProspectingProviderRegistry";
}
