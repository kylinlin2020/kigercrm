namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Exact legal-entity identity match returned by an authoritative LEI registry.</summary>
public sealed record CrmProspectingLegalEntityMatch(
    string Lei,
    string LegalName,
    string? EntityStatus,
    string? RegistrationStatus,
    string? CountryCode,
    string? RegisteredAs,
    string SourceUrl,
    string RawPayloadHash,
    DateTimeOffset RetrievedAtUtc);

/// <summary>
/// Resolves an already-discovered candidate name against authoritative legal-entity data.
/// Implementations must never create CRM Leads or treat fuzzy matches as verified identities.
/// </summary>
public interface ICrmProspectingIdentityResolver
{
    Task<CrmProspectingLegalEntityMatch?> ResolveExactAsync(
        string companyName,
        CancellationToken cancellationToken = default);
}
