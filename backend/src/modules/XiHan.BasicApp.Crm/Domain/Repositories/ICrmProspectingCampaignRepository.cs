using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Strictly tenant-scoped campaign repository.</summary>
public interface ICrmProspectingCampaignRepository : ISaasRepository<SysCrmProspectingCampaign>
{
}
