using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>SqlSugar implementation; tenant and soft-delete filters come from SaasRepository.</summary>
public sealed class CrmLeadRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmLead>(clientResolver), ICrmLeadRepository
{
    public async Task<IReadOnlyList<SysCrmLead>> GetDuplicateMatchesAsync(
        IReadOnlyCollection<string> normalizedEmails,
        IReadOnlyCollection<string> websiteHosts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedEmails);
        ArgumentNullException.ThrowIfNull(websiteHosts);
        cancellationToken.ThrowIfCancellationRequested();

        const int batchSize = 300;
        var matches = new Dictionary<long, SysCrmLead>();
        foreach (var batch in normalizedEmails.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keys = batch.ToArray();
            var rows = await CreateQueryable()
                .Where(lead => lead.DuplicateEmailNormalized != null && keys.Contains(lead.DuplicateEmailNormalized))
                .ToListAsync(cancellationToken);
            foreach (var row in rows) matches.TryAdd(row.BasicId, row);
        }

        foreach (var batch in websiteHosts.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keys = batch.ToArray();
            var rows = await CreateQueryable()
                .Where(lead => lead.WebsiteHost != null && keys.Contains(lead.WebsiteHost))
                .ToListAsync(cancellationToken);
            foreach (var row in rows) matches.TryAdd(row.BasicId, row);
        }

        return matches.Values.ToArray();
    }
}
