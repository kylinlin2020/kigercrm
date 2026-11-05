using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;
namespace XiHan.BasicApp.Crm.Application.Contracts;
public interface ICrmActivityAppService : IApplicationService
{
    Task<CrmActivityDto> CreateAsync(CrmActivityCreateDto input, CancellationToken cancellationToken = default);
    Task<CrmActivityDto> UpdateAsync(CrmActivityUpdateDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}