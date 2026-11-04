namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Immutable input snapshot passed to a prospecting search provider.</summary>
public sealed record CrmProspectingProviderRequest(
    long RunId,
    string SearchObjective,
    string ProductScope,
    string TargetMarkets,
    string IdealCustomerProfile,
    string ProviderOptionsJson,
    IReadOnlyList<string> ExcludedDomains);

/// <summary>One raw result returned by a provider; it is not yet a CRM lead.</summary>
public sealed record CrmProspectingRawRecord(
    string ExternalId,
    string Source,
    string Title,
    string? CompanyName,
    string? Website,
    string? ContactName,
    string? BusinessEmail,
    string? Summary)
{
    /// <summary>Canonical source record URL, not a search-result redirect.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Provider-reported publication time, normalized to UTC by the adapter.</summary>
    public DateTimeOffset? PublishedAtUtc { get; init; }

    /// <summary>UTC time at which the provider response was retrieved.</summary>
    public DateTimeOffset? RetrievedAtUtc { get; init; }

    /// <summary>Provider payload SHA-256; do not store raw provider payloads by default.</summary>
    public string? RawPayloadHash { get; init; }

    /// <summary>Evidence classification such as direct, registry, or provider_asserted.</summary>
    public string? EvidenceType { get; init; }
}

/// <summary>Provider execution result before normalization, deduplication and human review.</summary>
public sealed record CrmProspectingProviderResult(
    IReadOnlyList<CrmProspectingRawRecord> Records,
    int FailureCount,
    IReadOnlyList<string> Warnings)
{
    /// <summary>False when the adapter stopped before exhausting the provider's available result pages.</summary>
    public bool IsComplete { get; init; } = true;
}

/// <summary>
/// Search-provider boundary. Implementations must honor cancellation, treat provider output as untrusted
/// data, and must not create or update CRM leads directly.
/// </summary>
public interface ICrmProspectingProvider
{
    string ProviderCode { get; }

    Task<CrmProspectingProviderResult> SearchAsync(
        CrmProspectingProviderRequest request,
        CancellationToken cancellationToken = default);
}
