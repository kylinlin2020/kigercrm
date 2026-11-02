using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Read operations for tenant CRM leads.</summary>
public interface ICrmLeadQueryService : IApplicationService
{
    Task<PageResultDtoBase<CrmLeadListItemDto>> GetPageAsync(CrmLeadPageQueryDto input, CancellationToken cancellationToken = default);
    Task<CrmLeadDetailDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default);
}
