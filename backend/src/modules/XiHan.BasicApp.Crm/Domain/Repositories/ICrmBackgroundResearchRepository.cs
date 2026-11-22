using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Storage for research reports and their structured evidence and opportunities.</summary>
public interface ICrmBackgroundResearchRepository
{
    Task<SysCrmBackgroundResearchReport?> GetCurrentByCustomerAsync(long customerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysCrmBackgroundResearchReport>> GetByCustomerAsync(long customerId, CancellationToken cancellationToken = default);

    Task<SysCrmBackgroundResearchReport> AddReportAsync(SysCrmBackgroundResearchReport report, CancellationToken cancellationToken = default);

    /// <summary>Mark the customer's current reports as superseded before inserting a new dossier.</summary>
    Task<bool> SupersedeAsync(long customerId, CancellationToken cancellationToken = default);

    Task AddEvidenceAsync(IEnumerable<SysCrmResearchEvidence> rows, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysCrmResearchEvidence>> GetEvidenceAsync(long reportId, CancellationToken cancellationToken = default);

    Task AddOpportunitiesAsync(IEnumerable<SysCrmMarketOpportunity> rows, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysCrmMarketOpportunity>> GetOpportunitiesAsync(long reportId, CancellationToken cancellationToken = default);

    Task<SysCrmMarketOpportunity?> GetOpportunityAsync(long id, CancellationToken cancellationToken = default);

    Task<bool> SetOpportunityStatusAsync(
        long id,
        CrmMarketOpportunityStatus from,
        CrmMarketOpportunityStatus to,
        CancellationToken cancellationToken = default);
}
