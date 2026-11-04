using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.DomainServices;

public interface ICrmProspectingDomainService
{
    Task<SysCrmProspectingCampaign> CreateCampaignAsync(SysCrmProspectingCampaign campaign, CancellationToken cancellationToken = default);
    Task<SysCrmProspectingCampaign> UpdateCampaignAsync(SysCrmProspectingCampaign campaign, CancellationToken cancellationToken = default);
    Task<SysCrmProspectingStrategy> CreateStrategyVersionAsync(SysCrmProspectingStrategy strategy, CancellationToken cancellationToken = default);
    Task<SysCrmProspectingStrategy> SubmitStrategyForApprovalAsync(long strategyId, CancellationToken cancellationToken = default);
    Task<SysCrmProspectingStrategy> DecideStrategyAsync(long strategyId, bool approve, long? approverId, CancellationToken cancellationToken = default);
    Task<SysCrmProspectingCampaign> ActivateStrategyAsync(long strategyId, CancellationToken cancellationToken = default);
    Task<SysCrmProspectingRun> CreateRunAsync(long campaignId, string requestKey, CancellationToken cancellationToken = default);
    Task<SysCrmProspectingRun?> GetRunAsync(long runId, CancellationToken cancellationToken = default);
    Task<SysCrmProspectingRun> CancelRunAsync(long runId, CancellationToken cancellationToken = default);
}
