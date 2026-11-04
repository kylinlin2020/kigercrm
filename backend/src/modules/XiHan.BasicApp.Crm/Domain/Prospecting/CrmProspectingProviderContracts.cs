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
    string? Summary);

/// <summary>Provider execution result before normalization, deduplication and human review.</summary>
public sealed record CrmProspectingProviderResult(
    IReadOnlyList<CrmProspectingRawRecord> Records,
    int FailureCount,
    IReadOnlyList<string> Warnings);

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
