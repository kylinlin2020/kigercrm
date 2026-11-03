using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmLeadAppService : IApplicationService
{
    Task<CrmLeadDetailDto> CreateAsync(CrmLeadCreateDto input, CancellationToken cancellationToken = default);
    Task<CrmLeadDetailDto> UpdateAsync(CrmLeadUpdateDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
