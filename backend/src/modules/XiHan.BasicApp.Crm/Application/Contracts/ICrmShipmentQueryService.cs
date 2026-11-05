using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmShipmentQueryService : IApplicationService
{
    Task<PageResultDtoBase<CrmShipmentListItemDto>> GetPageAsync(CrmShipmentPageQueryDto input, CancellationToken cancellationToken = default);
    Task<CrmShipmentDetailDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default);
    Task<PageResultDtoBase<CrmShipmentLineDto>> GetLinesPageAsync(CrmShipmentLinePageQueryDto input, CancellationToken cancellationToken = default);
}
