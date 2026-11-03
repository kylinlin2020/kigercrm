using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmContactAppService : IApplicationService
{
    Task<CrmContactDto> CreateAsync(CrmContactCreateDto input, CancellationToken cancellationToken = default);
    Task<CrmContactDto> UpdateAsync(CrmContactUpdateDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
