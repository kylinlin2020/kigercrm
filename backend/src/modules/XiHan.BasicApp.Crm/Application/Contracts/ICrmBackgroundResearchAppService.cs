using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Local, auditable customer background research and market opportunity review.</summary>
public interface ICrmBackgroundResearchAppService : IApplicationService
{
    Task<CrmBackgroundResearchDetailDto> GenerateAsync(long customerId, CancellationToken cancellationToken = default);

    Task<CrmBackgroundResearchDetailDto?> GetCurrentAsync(long customerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CrmBackgroundResearchReportDto>> GetHistoryAsync(long customerId, CancellationToken cancellationToken = default);

    Task<CrmMarketOpportunityDto> ReviewOpportunityAsync(
        CrmMarketOpportunityReviewDto input,
        CancellationToken cancellationToken = default);
}
