using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped prospecting review candidate repository.</summary>
public interface ICrmProspectingCandidateRepository : ISaasRepository<SysCrmProspectingCandidate>
{
    /// <summary>Atomically transition an approved candidate to Converting; false means another actor already claimed it.</summary>
    Task<bool> TryClaimForConversionAsync(long candidateId, CancellationToken cancellationToken = default);
}
