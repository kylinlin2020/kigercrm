using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmSalesOrderAppService : IApplicationService
{
    Task<CrmSalesOrderListItemDto> ChangeStatusAsync(CrmSalesOrderStatusUpdateDto input, CancellationToken cancellationToken = default);
}
