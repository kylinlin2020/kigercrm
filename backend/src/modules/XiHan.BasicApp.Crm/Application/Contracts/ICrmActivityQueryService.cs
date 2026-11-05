using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;
using XiHan.Framework.Domain.Shared.Paging.Dtos;
namespace XiHan.BasicApp.Crm.Application.Contracts;
public interface ICrmActivityQueryService : IApplicationService
{
    Task<PageResultDtoBase<CrmActivityListItemDto>> GetPageAsync(CrmActivityPageQueryDto input, CancellationToken cancellationToken = default);
    Task<CrmActivityDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default);
}