using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingCandidateRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingCandidate>(clientResolver), ICrmProspectingCandidateRepository

{
    public async Task<IReadOnlyList<SysCrmProspectingCandidate>> GetLatestByRunAsync(long runId, CrmProspectingCandidateStatus? status, int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var take = Math.Clamp(limit, 1, 500);
        return await CreateQueryable()
            .Where(candidate => candidate.RunId == runId && (!status.HasValue || candidate.Status == status.Value))
            .OrderByDescending(candidate => candidate.CreatedTime)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryClaimForConversionAsync(long candidateId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await UpdateAsync(
            candidate => new SysCrmProspectingCandidate { Status = CrmProspectingCandidateStatus.Converting },
            candidate => candidate.BasicId == candidateId && candidate.Status == CrmProspectingCandidateStatus.Approved,
            cancellationToken);
    }
}
