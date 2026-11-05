using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmOpportunityAppService : IApplicationService
{
    Task<CrmOpportunityDto> CreateAsync(CrmOpportunityCreateDto input, CancellationToken cancellationToken = default);
    Task<CrmOpportunityDto> UpdateAsync(CrmOpportunityUpdateDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
