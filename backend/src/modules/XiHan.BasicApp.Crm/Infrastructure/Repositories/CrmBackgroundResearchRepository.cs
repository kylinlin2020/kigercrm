using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>Tenant-scoped storage composed of three entity repositories behind one aggregate API.</summary>
public sealed class CrmBackgroundResearchRepository(ISqlSugarClientResolver clientResolver)
    : ICrmBackgroundResearchRepository
{
    private readonly ReportRepository _reports = new(clientResolver);
    private readonly EvidenceRepository _evidence = new(clientResolver);
    private readonly OpportunityRepository _opportunities = new(clientResolver);

    public Task<SysCrmBackgroundResearchReport?> GetCurrentByCustomerAsync(
        long customerId,
        CancellationToken cancellationToken = default)
        => _reports.FirstByCustomerAsync(customerId, CrmResearchReportStatus.Current, cancellationToken);

    public Task<IReadOnlyList<SysCrmBackgroundResearchReport>> GetByCustomerAsync(
        long customerId,
        CancellationToken cancellationToken = default)
        => _reports.ByCustomerAsync(customerId, cancellationToken);

    public Task<SysCrmBackgroundResearchReport> AddReportAsync(
        SysCrmBackgroundResearchReport report,
        CancellationToken cancellationToken = default)
        => _reports.AddAsync(report, cancellationToken);

    public Task<bool> SupersedeAsync(long customerId, CancellationToken cancellationToken = default)
        => _reports.UpdateAsync(
            report => new SysCrmBackgroundResearchReport { Status = CrmResearchReportStatus.Superseded },
            report => report.CustomerId == customerId && report.Status == CrmResearchReportStatus.Current,
            cancellationToken);

    public async Task AddEvidenceAsync(
        IEnumerable<SysCrmResearchEvidence> rows,
        CancellationToken cancellationToken = default)
    {
        foreach (var row in rows) await _evidence.AddAsync(row, cancellationToken);
    }

    public Task<IReadOnlyList<SysCrmResearchEvidence>> GetEvidenceAsync(
        long reportId,
        CancellationToken cancellationToken = default)
        => _evidence.ByReportAsync(reportId, cancellationToken);

    public async Task AddOpportunitiesAsync(
        IEnumerable<SysCrmMarketOpportunity> rows,
        CancellationToken cancellationToken = default)
    {
        foreach (var row in rows) await _opportunities.AddAsync(row, cancellationToken);
    }

    public Task<IReadOnlyList<SysCrmMarketOpportunity>> GetOpportunitiesAsync(
        long reportId,
        CancellationToken cancellationToken = default)
        => _opportunities.ByReportAsync(reportId, cancellationToken);

    public Task<SysCrmMarketOpportunity?> GetOpportunityAsync(
        long id,
        CancellationToken cancellationToken = default)
        => _opportunities.GetByIdAsync(id, cancellationToken);

    public Task<bool> SetOpportunityStatusAsync(
        long id,
        CrmMarketOpportunityStatus from,
        CrmMarketOpportunityStatus to,
        CancellationToken cancellationToken = default)
        => _opportunities.UpdateAsync(
            opportunity => new SysCrmMarketOpportunity { Status = to },
            opportunity => opportunity.BasicId == id && opportunity.Status == from,
            cancellationToken);

    private sealed class ReportRepository(ISqlSugarClientResolver resolver)
        : SaasRepository<SysCrmBackgroundResearchReport>(resolver)
    {
        internal async Task<SysCrmBackgroundResearchReport?> FirstByCustomerAsync(
            long customerId, CrmResearchReportStatus status, CancellationToken cancellationToken)
            => (await CreateQueryable()
                .Where(report => report.CustomerId == customerId && report.Status == status)
                .OrderBy(report => report.GeneratedAtUtc, OrderByType.Desc)
                .Take(1).ToListAsync(cancellationToken)).FirstOrDefault();

        internal async Task<IReadOnlyList<SysCrmBackgroundResearchReport>> ByCustomerAsync(
            long customerId, CancellationToken cancellationToken)
            => await CreateQueryable()
                .Where(report => report.CustomerId == customerId)
                .OrderBy(report => report.GeneratedAtUtc, OrderByType.Desc)
                .ToListAsync(cancellationToken);
    }

    private sealed class EvidenceRepository(ISqlSugarClientResolver resolver)
        : SaasRepository<SysCrmResearchEvidence>(resolver)
    {
        internal async Task<IReadOnlyList<SysCrmResearchEvidence>> ByReportAsync(
            long reportId, CancellationToken cancellationToken)
            => await CreateQueryable()
                .Where(evidence => evidence.ReportId == reportId)
                .OrderBy(evidence => evidence.Source)
                .OrderBy(evidence => evidence.FactKey)
                .ToListAsync(cancellationToken);
    }

    private sealed class OpportunityRepository(ISqlSugarClientResolver resolver)
        : SaasRepository<SysCrmMarketOpportunity>(resolver)
    {
        internal async Task<IReadOnlyList<SysCrmMarketOpportunity>> ByReportAsync(
            long reportId, CancellationToken cancellationToken)
            => await CreateQueryable()
                .Where(opportunity => opportunity.ReportId == reportId)
                .OrderByDescending(opportunity => opportunity.PriorityScore)
                .OrderBy(opportunity => opportunity.Title)
                .ToListAsync(cancellationToken);
    }
}
