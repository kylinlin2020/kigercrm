using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Read-only CRM effect reports. All figures are tenant- and data-scope-filtered.</summary>
public interface ICrmDashboardQueryService : IApplicationService
{
    Task<CrmDashboardOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<CrmDashboardReportDto> GetLeadFunnelAsync(CancellationToken cancellationToken = default);
    Task<CrmDashboardReportDto> GetProspectSourcesAsync(CancellationToken cancellationToken = default);
    Task<CrmDashboardReportDto> GetOpportunityStagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Owner ranking of accessible Leads/Opportunities within an optional UTC creation-time window.</summary>
    Task<CrmDashboardReportDto> GetTeamRankingAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Follow-up freshness buckets for accessible Leads within an optional UTC creation-time window.</summary>
    Task<CrmDashboardReportDto> GetFollowUpHealthAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default);
}