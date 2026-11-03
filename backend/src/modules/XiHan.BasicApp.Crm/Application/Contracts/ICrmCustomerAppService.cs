using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmCustomerAppService : IApplicationService
{
    Task<CrmCustomerDetailDto> CreateAsync(CrmCustomerCreateDto input, CancellationToken cancellationToken = default);
    Task<CrmCustomerDetailDto> UpdateAsync(CrmCustomerUpdateDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
