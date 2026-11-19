using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Chat.Domain.Repositories;

public interface IBaileysSessionRepository : ISaasRepository<SysBaileysSession>
{
    Task<IReadOnlyList<SysBaileysSession>> GetAllAsync(CancellationToken cancellationToken = default);
}
