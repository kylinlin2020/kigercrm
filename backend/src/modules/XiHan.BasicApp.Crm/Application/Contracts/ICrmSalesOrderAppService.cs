using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmSalesOrderAppService : IApplicationService
{
    Task<CrmSalesOrderListItemDto> CreateAsync(CrmSalesOrderCreateDto input, CancellationToken cancellationToken = default);
    Task<CrmSalesOrderListItemDto> UpdateDraftAsync(CrmSalesOrderUpdateDto input, CancellationToken cancellationToken = default);
    Task<CrmSalesOrderListItemDto> ChangeStatusAsync(CrmSalesOrderStatusUpdateDto input, CancellationToken cancellationToken = default);
}
