using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmShipmentAppService : IApplicationService
{
    Task<CrmShipmentDto> CreateAsync(CrmShipmentCreateDto input, CancellationToken cancellationToken = default);
    Task<CrmShipmentDto> ChangeStatusAsync(CrmShipmentStatusUpdateDto input, CancellationToken cancellationToken = default);
}
