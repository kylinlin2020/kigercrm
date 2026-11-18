using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Append-only qualification report repository.</summary>
public interface ICrmProspectingVerificationReportRepository : ISaasRepository<SysCrmProspectingVerificationReport>
{
    Task<SysCrmProspectingVerificationReport?> GetLatestByCandidateAsync(long candidateId, CancellationToken cancellationToken = default);
}
