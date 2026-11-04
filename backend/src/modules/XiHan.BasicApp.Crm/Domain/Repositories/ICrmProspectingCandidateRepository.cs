using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped prospecting review candidate repository.</summary>
public interface ICrmProspectingCandidateRepository : ISaasRepository<SysCrmProspectingCandidate>
{
    /// <summary>Find one Candidate by the exact tenant-scoped Run/provider/external identity tuple.</summary>
    Task<SysCrmProspectingCandidate?> GetByIdentityAsync(long runId, string providerCode, string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find non-rejected Candidates from earlier Runs that match exact provider identities,
    /// normalized emails or website hosts. Used to suppress repeat prospecting across Runs.
    /// </summary>
    Task<IReadOnlyList<SysCrmProspectingCandidate>> GetCrossRunDuplicateMatchesAsync(
        long runId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> externalIdsByProvider,
        IReadOnlyList<string> normalizedEmails,
        IReadOnlyList<string> websiteHosts,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically transition an approved candidate to Converting; false means another actor already claimed it.</summary>
    Task<bool> TryClaimForConversionAsync(long candidateId, CancellationToken cancellationToken = default);

    /// <summary>Atomically review a pending candidate; duplicate approval requires an explicit override.</summary>
    Task<bool> TryReviewAsync(long candidateId, bool approve, bool overrideDuplicate, CancellationToken cancellationToken = default);

    /// <summary>Read the newest candidates for one Run with optional status and database-side limit.</summary>
    Task<IReadOnlyList<SysCrmProspectingCandidate>> GetLatestByRunAsync(long runId, CrmProspectingCandidateStatus? status, int limit, CancellationToken cancellationToken = default);
}
