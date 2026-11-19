using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.BasicApp.Chat.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Chat.Infrastructure.Repositories;

public sealed class WecomBindingRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysWecomAccountBinding>(clientResolver), IWecomBindingRepository
{
    public async Task<SysWecomAccountBinding?> FindRouteAnyTenantAsync(
        string corpId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corpId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var rows = await CreateQueryable().ClearFilter().Where(item =>
                item.CorpId == corpId && item.AgentId == agentId
                && item.Status == WecomBindingStatus.Active)
            .Take(1)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SysWecomAccountBinding>> GetAllAsync(CancellationToken cancellationToken = default)
        => await CreateQueryable().OrderByDescending(item => item.CreatedTime).Take(100).ToListAsync(cancellationToken);
}

public sealed class WecomCommandAuditRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysWecomCommandAudit>(clientResolver), IWecomCommandAuditRepository
{
    public async Task<IReadOnlyList<SysWecomCommandAudit>> GetRecentAsync(int take, CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        return await CreateQueryable().OrderByDescending(item => item.CreatedTime).Take(take).ToListAsync(cancellationToken);
    }
}
