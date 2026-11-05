using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmOpportunityQueryService : IApplicationService
{
    Task<PageResultDtoBase<CrmOpportunityListItemDto>> GetPageAsync(CrmOpportunityPageQueryDto input, CancellationToken cancellationToken = default);
    Task<CrmOpportunityDetailDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default);
}
