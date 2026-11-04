using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped prospecting review candidate repository.</summary>
public interface ICrmProspectingCandidateRepository : ISaasRepository<SysCrmProspectingCandidate>
{
}
