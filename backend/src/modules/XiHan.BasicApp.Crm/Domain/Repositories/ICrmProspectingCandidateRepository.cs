using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped prospecting review candidate repository.</summary>
public interface ICrmProspectingCandidateRepository : ISaasRepository<SysCrmProspectingCandidate>
{
    /// <summary>Atomically transition an approved candidate to Converting; false means another actor already claimed it.</summary>
    Task<bool> TryClaimForConversionAsync(long candidateId, CancellationToken cancellationToken = default);

    /// <summary>Atomically review a pending candidate; duplicate approval requires an explicit override.</summary>
    Task<bool> TryReviewAsync(long candidateId, bool approve, bool overrideDuplicate, CancellationToken cancellationToken = default);

    /// <summary>Read the newest candidates for one Run with optional status and database-side limit.</summary>
    Task<IReadOnlyList<SysCrmProspectingCandidate>> GetLatestByRunAsync(long runId, CrmProspectingCandidateStatus? status, int limit, CancellationToken cancellationToken = default);
}
