using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped repository for CRM leads.</summary>
public interface ICrmLeadRepository : ISaasRepository<SysCrmLead>
{
}
