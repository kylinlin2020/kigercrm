using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped immutable prospecting score history.</summary>
public interface ICrmProspectingScoreRepository : ISaasRepository<SysCrmProspectingScore>
{
    Task<SysCrmProspectingScore?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysCrmProspectingScore>> GetLatestByCandidateAsync(
        long candidateId, int limit = 20, CancellationToken cancellationToken = default);
}
