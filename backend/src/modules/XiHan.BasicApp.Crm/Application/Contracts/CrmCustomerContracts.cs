using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmCustomerQueryService : IApplicationService
{
    Task<PageResultDtoBase<CrmCustomerListItemDto>> GetPageAsync(CrmCustomerPageQueryDto input, CancellationToken cancellationToken = default);
    Task<CrmCustomerDetailDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default);
}
