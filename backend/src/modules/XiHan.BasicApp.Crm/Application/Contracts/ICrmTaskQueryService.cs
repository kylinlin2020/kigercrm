using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;
using XiHan.Framework.Domain.Shared.Paging.Dtos;
namespace XiHan.BasicApp.Crm.Application.Contracts;
public interface ICrmTaskQueryService : IApplicationService
{
    Task<PageResultDtoBase<CrmTaskListItemDto>> GetPageAsync(CrmTaskPageQueryDto input, CancellationToken cancellationToken = default);
    Task<CrmTaskDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default);
}