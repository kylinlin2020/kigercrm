using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Chat.Domain.Repositories;

public interface IWecomBindingRepository : ISaasRepository<SysWecomAccountBinding>
{
    Task<SysWecomAccountBinding?> FindRouteAnyTenantAsync(string corpId, string agentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysWecomAccountBinding>> GetAllAsync(CancellationToken cancellationToken = default);
}

public interface IWecomCommandAuditRepository : ISaasRepository<SysWecomCommandAudit>
{
    Task<IReadOnlyList<SysWecomCommandAudit>> GetRecentAsync(int take, CancellationToken cancellationToken = default);
}
