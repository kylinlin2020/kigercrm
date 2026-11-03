using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>SqlSugar implementation; tenant and soft-delete filters come from SaasRepository.</summary>
public sealed class CrmShipmentLineRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmShipmentLine>(clientResolver), ICrmShipmentLineRepository { }
