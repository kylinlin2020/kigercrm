using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;
namespace XiHan.BasicApp.Crm.Application.Contracts;
public interface ICrmTaskAppService : IApplicationService
{
    Task<CrmTaskDto> CreateAsync(CrmTaskCreateDto input, CancellationToken cancellationToken = default);
    Task<CrmTaskDto> UpdateAsync(CrmTaskUpdateDto input, CancellationToken cancellationToken = default);
    Task<CrmTaskDto> ChangeStatusAsync(CrmTaskStatusUpdateDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}