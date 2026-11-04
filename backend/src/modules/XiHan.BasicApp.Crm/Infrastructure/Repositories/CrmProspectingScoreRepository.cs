using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingScoreRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingScore>(clientResolver), ICrmProspectingScoreRepository
{
    public async Task<IReadOnlyList<SysCrmProspectingScore>> GetLatestByCandidateAsync(
        long candidateId, int limit = 20, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));

        return await CreateQueryable()
            .Where(score => score.CandidateId == candidateId)
            .OrderByDescending(score => score.EvaluatedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
    }
}
