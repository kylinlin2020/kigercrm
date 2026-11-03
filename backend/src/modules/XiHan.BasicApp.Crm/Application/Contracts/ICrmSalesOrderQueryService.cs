using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmSalesOrderQueryService : IApplicationService
{
    Task<PageResultDtoBase<CrmSalesOrderListItemDto>> GetPageAsync(CrmSalesOrderPageQueryDto input, CancellationToken cancellationToken = default);
}
