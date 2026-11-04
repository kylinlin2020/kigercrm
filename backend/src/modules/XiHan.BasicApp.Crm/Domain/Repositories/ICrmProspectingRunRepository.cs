using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Strictly tenant-scoped prospecting run repository.</summary>
public interface ICrmProspectingRunRepository : ISaasRepository<SysCrmProspectingRun>
{
}
