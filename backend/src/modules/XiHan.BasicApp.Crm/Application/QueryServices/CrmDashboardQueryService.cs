using XiHan.BasicApp.Crm.Application;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

/// <summary>
/// Read-only effect reports for the CRM dashboard. Lead/opportunity figures honour the caller's
/// user data scope; prospecting/Run figures are tenant-scoped by the repository query filter.
/// No report writes anything or invokes external services.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Dashboard")]
public sealed class CrmDashboardQueryService(
    ICrmLeadRepository leads,
    ICrmOpportunityRepository opportunities,
    ICrmProspectingCandidateRepository candidates,
    ICrmProspectingRunRepository runs,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmDashboardQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmDashboardOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var scope = await dataScope.ResolveAccessibleUsersAsync(DateTimeOffset.UtcNow, cancellationToken);
        var unrestricted = scope.Unrestricted;
        var userIds = scope.UserIds;
        var accessibleLeads = await leads.GetListAsync(item => unrestricted || (item.OwnerUserId > 0 && userIds.Contains(item.OwnerUserId.Value)), cancellationToken);
        var accessibleOpportunities = await opportunities.GetListAsync(
            item => unrestricted || (item.OwnerUserId > 0 && userIds.Contains(item.OwnerUserId.Value)), cancellationToken);
        var prospectingCandidates = await candidates.GetListAsync(_ => true, cancellationToken);
        var prospectingRuns = await runs.GetListAsync(_ => true, cancellationToken);
        return new CrmDashboardOverviewDto
        {
            LeadsTotal = accessibleLeads.Count,
            OpportunitiesTotal = accessibleOpportunities.Count,
            OpportunityAmount = accessibleOpportunities.Sum(item => item.Amount),
            ProspectsTotal = prospectingCandidates.Count,
            RunsTotal = prospectingRuns.Count,
            ActiveRuns = prospectingRuns.Count(item => item.Status is CrmProspectingRunStatus.Queued
                or CrmProspectingRunStatus.Running
                or CrmProspectingRunStatus.WaitingForExtractions
                or CrmProspectingRunStatus.Paused),
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmDashboardReportDto> GetLeadFunnelAsync(CancellationToken cancellationToken = default)
    {
        var scope = await dataScope.ResolveAccessibleUsersAsync(DateTimeOffset.UtcNow, cancellationToken);
        var unrestricted = scope.Unrestricted;
        var userIds = scope.UserIds;
        var rows = await leads.GetListAsync(
            item => unrestricted || (item.OwnerUserId > 0 && userIds.Contains(item.OwnerUserId.Value)), cancellationToken);
        return Build(rows.GroupBy(item => item.Status.ToString()), rows.Count);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmDashboardReportDto> GetProspectSourcesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await candidates.GetListAsync(_ => true, cancellationToken);
        return Build(rows.GroupBy(item => string.IsNullOrWhiteSpace(item.ProviderCode) ? "unknown" : item.ProviderCode), rows.Count);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmDashboardReportDto> GetOpportunityStagesAsync(CancellationToken cancellationToken = default)
    {
        var scope = await dataScope.ResolveAccessibleUsersAsync(DateTimeOffset.UtcNow, cancellationToken);
        var unrestricted = scope.Unrestricted;
        var userIds = scope.UserIds;
        var rows = await opportunities.GetListAsync(
            item => unrestricted || (item.OwnerUserId > 0 && userIds.Contains(item.OwnerUserId.Value)), cancellationToken);
        var groups = rows.GroupBy(item => item.Stage.ToString())
            .Select(group => new CrmDashboardBucketDto
            {
                Key = group.Key,
                Label = group.Key,
                Count = group.Count(),
                Amount = group.Sum(item => item.Amount)
            })
            .OrderByDescending(bucket => bucket.Count)
            .ToArray();
        var total = rows.Count;
        foreach (var bucket in groups)
            bucket.SharePercent = total == 0 ? 0 : (int)Math.Round(bucket.Count * 100d / total);
        return new CrmDashboardReportDto
        {
            Buckets = groups,
            Total = total,
            TotalAmount = rows.Sum(item => item.Amount)
        };
    }

    private static CrmDashboardReportDto Build(
        IEnumerable<IGrouping<string, SysCrmProspectingCandidate>> groups,
        int total)
        => Build(groups.Select(group => new CrmDashboardBucketDto
        {
            Key = group.Key,
            Label = group.Key,
            Count = group.Count()
        }), total, 0);

    private static CrmDashboardReportDto Build(
        IEnumerable<IGrouping<string, SysCrmLead>> groups,
        int total)
        => Build(groups.Select(group => new CrmDashboardBucketDto
        {
            Key = group.Key,
            Label = group.Key,
            Count = group.Count()
        }), total, 0);

    private static CrmDashboardReportDto Build(IEnumerable<CrmDashboardBucketDto> buckets, int total, decimal totalAmount)
    {
        var rows = buckets.OrderByDescending(bucket => bucket.Count).ToArray();
        foreach (var bucket in rows)
            bucket.SharePercent = total == 0 ? 0 : (int)Math.Round(bucket.Count * 100d / total);
        return new CrmDashboardReportDto { Buckets = rows, Total = total, TotalAmount = totalAmount };
    }
}
