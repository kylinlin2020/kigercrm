using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Campaign and versioned prospecting-strategy management.</summary>
public interface ICrmProspectingAppService : IApplicationService
{
    Task<CrmProspectingCampaignDto> CreateCampaignAsync(CrmProspectingCampaignInputDto input, CancellationToken cancellationToken = default);
    Task<CrmProspectingCampaignDto> UpdateCampaignAsync(CrmProspectingCampaignInputDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmProspectingCampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken = default);
    Task<CrmProspectingCampaignDto?> GetCampaignAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmProspectingStrategyDto>> GetStrategiesAsync(long campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmProspectingRunDto>> GetCampaignRunsAsync(long campaignId, CancellationToken cancellationToken = default);
    Task<CrmProspectingStrategyDto> CreateStrategyVersionAsync(CrmProspectingStrategyInputDto input, CancellationToken cancellationToken = default);
    Task<CrmProspectingStrategyDto> SubmitStrategyForApprovalAsync(long strategyId, CancellationToken cancellationToken = default);
    Task<CrmProspectingStrategyDto> DecideStrategyAsync(CrmProspectingStrategyDecisionDto input, CancellationToken cancellationToken = default);
    Task<CrmProspectingCampaignDto> ActivateStrategyAsync(long strategyId, CancellationToken cancellationToken = default);
    Task<CrmProspectingRunDto> StartRunAsync(CrmProspectingRunStartDto input, CancellationToken cancellationToken = default);
    Task<CrmProspectingRunDto?> GetRunAsync(long runId, CancellationToken cancellationToken = default);
    Task<CrmProspectingRunDto> CancelRunAsync(long runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmProspectingCandidateDto>> GetRunCandidatesAsync(long runId, int? status = null, CancellationToken cancellationToken = default);
    Task<CrmProspectingCandidateReviewDetailDto> GetCandidateReviewDetailAsync(long candidateId, CancellationToken cancellationToken = default);
    Task<CrmProspectingCandidateDto> ReviewCandidateAsync(CrmProspectingCandidateReviewDto input, CancellationToken cancellationToken = default);
    Task<CrmLeadDetailDto> ConvertApprovedCandidateToLeadAsync(long candidateId, CancellationToken cancellationToken = default);
}
