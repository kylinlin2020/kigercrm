using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped immutable prospecting evidence repository.</summary>
public interface ICrmProspectingEvidenceRepository : ISaasRepository<SysCrmProspectingEvidence>
{
    Task<IReadOnlyList<SysCrmProspectingEvidence>> GetByCandidateAsync(long candidateId, CancellationToken cancellationToken = default);
}
