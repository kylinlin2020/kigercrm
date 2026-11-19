using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Chat.Infrastructure.Repositories;

public sealed class BaileysSessionRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysBaileysSession>(clientResolver), IBaileysSessionRepository
{
    public async Task<IReadOnlyList<SysBaileysSession>> GetAllAsync(CancellationToken cancellationToken = default)
        => await CreateQueryable().OrderByDescending(item => item.CreatedTime).Take(100).ToListAsync(cancellationToken);
}
