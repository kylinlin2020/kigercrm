using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmContactQueryService : IApplicationService
{
    Task<PageResultDtoBase<CrmContactListItemDto>> GetPageAsync(CrmContactPageQueryDto input, CancellationToken cancellationToken = default);
    Task<CrmContactDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default);
}
