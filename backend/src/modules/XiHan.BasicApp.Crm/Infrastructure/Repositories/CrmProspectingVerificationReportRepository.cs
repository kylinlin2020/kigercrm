using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingVerificationReportRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingVerificationReport>(clientResolver), ICrmProspectingVerificationReportRepository
{
    public async Task<SysCrmProspectingVerificationReport?> GetLatestByCandidateAsync(long candidateId, CancellationToken cancellationToken = default)
    {
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await CreateQueryable()
            .Where(report => report.CandidateId == candidateId)
            .OrderByDescending(report => report.CreatedTime)
            .Take(1)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }
}
