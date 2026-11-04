using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Strictly tenant-scoped immutable strategy-version repository.</summary>
public interface ICrmProspectingStrategyRepository : ISaasRepository<SysCrmProspectingStrategy>
{
}
