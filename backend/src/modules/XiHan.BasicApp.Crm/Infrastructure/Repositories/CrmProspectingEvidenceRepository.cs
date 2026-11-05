using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingEvidenceRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingEvidence>(clientResolver), ICrmProspectingEvidenceRepository
{
    public async Task<IReadOnlyList<SysCrmProspectingEvidence>> GetByCandidateAsync(
        long candidateId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));

        return await CreateQueryable()
            .Where(evidence => evidence.CandidateId == candidateId)
            .OrderBy(evidence => evidence.FieldName)
            .ThenBy(evidence => evidence.RetrievedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
