using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped repository for CRM leads.</summary>
public interface ICrmLeadRepository : ISaasRepository<SysCrmLead>
{
    /// <summary>Find existing tenant Leads matching normalized email or website keys in bounded database batches.</summary>
    Task<IReadOnlyList<SysCrmLead>> GetDuplicateMatchesAsync(
        IReadOnlyCollection<string> normalizedEmails,
        IReadOnlyCollection<string> websiteHosts,
        CancellationToken cancellationToken = default);
}
