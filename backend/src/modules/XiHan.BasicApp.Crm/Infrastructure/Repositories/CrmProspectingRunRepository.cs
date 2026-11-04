using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingRunRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingRun>(clientResolver), ICrmProspectingRunRepository
{
}
