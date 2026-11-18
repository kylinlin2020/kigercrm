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
    Task<CrmProspectingRunDto> PauseRunAsync(long runId, CancellationToken cancellationToken = default);
    Task<CrmProspectingRunDto> ResumeRunAsync(long runId, CancellationToken cancellationToken = default);
    Task<CrmProspectingCandidateReviewDetailDto> GetCandidateReviewDetailAsync(long candidateId, CancellationToken cancellationToken = default);
    Task<CrmProspectingCandidateDto> ReviewCandidateAsync(CrmProspectingCandidateReviewDto input, CancellationToken cancellationToken = default);
    Task<CrmLeadDetailDto> ConvertApprovedCandidateToLeadAsync(long candidateId, CancellationToken cancellationToken = default);

    /// <summary>Apply one approve/reject decision to a bounded id list, item by item under per-item commits.</summary>
    Task<CrmProspectingBulkResultDto> BulkReviewCandidatesAsync(CrmProspectingCandidateBulkReviewDto input, CancellationToken cancellationToken = default);

    /// <summary>Convert a bounded id list of Approved candidates to Leads; already-converted items are skipped and reported.</summary>
    Task<CrmProspectingBulkResultDto> BulkConvertCandidatesAsync(CrmProspectingCandidateBulkDto input, CancellationToken cancellationToken = default);

    /// <summary>Shortlist every listed candidate; already-shortlisted items are idempotent successes.</summary>
    Task<CrmProspectingBulkResultDto> ShortlistCandidatesAsync(CrmProspectingCandidateBulkDto input, CancellationToken cancellationToken = default);

    /// <summary>Remove every listed candidate from the shortlist; already-clean items are idempotent successes.</summary>
    Task<CrmProspectingBulkResultDto> UnshortlistCandidatesAsync(CrmProspectingCandidateBulkDto input, CancellationToken cancellationToken = default);

    /// <summary>Freeze a deterministic Super Search plan as a durable, idempotent mission.</summary>
    Task<CrmProspectingSuperSearchMissionDto> SaveSuperSearchMissionAsync(CrmProspectingSuperSearchMissionCreateDto input, CancellationToken cancellationToken = default);

    /// <summary>Recent missions of one campaign under the same record authorization as its Run history.</summary>
    Task<IReadOnlyList<CrmProspectingSuperSearchMissionDto>> GetSuperSearchMissionsAsync(long campaignId, CancellationToken cancellationToken = default);

    /// <summary>Create a mission's Run through the existing approved pipeline; replay returns the same Run.</summary>
    Task<CrmProspectingRunDto> CreateRunFromSuperSearchMissionAsync(long missionId, CancellationToken cancellationToken = default);
}
