using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmQuoteQueryService : IApplicationService
{
    Task<PageResultDtoBase<CrmQuoteListItemDto>> GetPageAsync(CrmQuotePageQueryDto input, CancellationToken cancellationToken = default);
    Task<CrmQuoteDetailDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default);
    Task<PageResultDtoBase<CrmQuoteLineDto>> GetLinesPageAsync(CrmQuoteLinePageQueryDto input, CancellationToken cancellationToken = default);
}
