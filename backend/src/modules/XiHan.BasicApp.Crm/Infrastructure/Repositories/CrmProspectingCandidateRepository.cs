using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingCandidateRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingCandidate>(clientResolver), ICrmProspectingCandidateRepository

{
    public async Task<SysCrmProspectingCandidate?> GetByIdentityAsync(long runId, string providerCode, string externalId, CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await CreateQueryable()
            .Where(candidate => candidate.RunId == runId &&
                candidate.ProviderCode == providerCode &&
                candidate.ExternalId == externalId)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (rows.Count > 1)
            throw new InvalidOperationException("The tenant contains multiple Candidates for one provider identity; the unique-key invariant is broken.");
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SysCrmProspectingCandidate>> GetLatestByRunAsync(long runId, CrmProspectingCandidateStatus? status, int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var take = Math.Clamp(limit, 1, 500);
        return await CreateQueryable()
            .Where(candidate => candidate.RunId == runId && (!status.HasValue || candidate.Status == status.Value))
            .OrderByDescending(candidate => candidate.CreatedTime)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SysCrmProspectingCandidate>> GetCrossRunDuplicateMatchesAsync(
        long runId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> externalIdsByProvider,
        IReadOnlyList<string> normalizedEmails,
        IReadOnlyList<string> websiteHosts,
        CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        ArgumentNullException.ThrowIfNull(externalIdsByProvider);
        ArgumentNullException.ThrowIfNull(normalizedEmails);
        ArgumentNullException.ThrowIfNull(websiteHosts);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = new List<SysCrmProspectingCandidate>();
        var seenIds = new HashSet<long>();

        // Match provider identity as a pair; do not use independent IN lists which could
        // create false cross-product matches between provider codes and external IDs.
        foreach (var pair in externalIdsByProvider)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ids = pair.Value.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();
            if (string.IsNullOrWhiteSpace(pair.Key) || ids.Length == 0) continue;

            var identityRows = await CreateQueryable()
                .Where(candidate => candidate.RunId != runId &&
                    candidate.Status != CrmProspectingCandidateStatus.Rejected &&
                    candidate.ProviderCode == pair.Key &&
                    ids.Contains(candidate.ExternalId))
                .Take(5000)
                .ToListAsync(cancellationToken);

            foreach (var row in identityRows)
                if (seenIds.Add(row.BasicId)) matches.Add(row);
        }

        if (normalizedEmails.Count > 0 || websiteHosts.Count > 0)
        {
            var emailRows = await CreateQueryable()
                .Where(candidate => candidate.RunId != runId &&
                    candidate.Status != CrmProspectingCandidateStatus.Rejected &&
                    ((normalizedEmails.Count > 0 && normalizedEmails.Contains(candidate.NormalizedEmail!)) ||
                     (websiteHosts.Count > 0 && websiteHosts.Contains(candidate.WebsiteHost!))))
                .Take(5000)
                .ToListAsync(cancellationToken);

            foreach (var row in emailRows)
                if (seenIds.Add(row.BasicId)) matches.Add(row);
        }

        return matches;
    }

    public async Task<bool> TryReviewAsync(long candidateId, bool approve, bool overrideDuplicate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await UpdateAsync(
            candidate => new SysCrmProspectingCandidate
            {
                Status = approve ? CrmProspectingCandidateStatus.Approved : CrmProspectingCandidateStatus.Rejected
            },
            candidate => candidate.BasicId == candidateId &&
                (candidate.Status == CrmProspectingCandidateStatus.PendingReview ||
                 ((!approve || overrideDuplicate) && candidate.Status == CrmProspectingCandidateStatus.Duplicate)),
            cancellationToken);
    }

    public async Task<bool> TryClaimForConversionAsync(long candidateId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await UpdateAsync(
            candidate => new SysCrmProspectingCandidate { Status = CrmProspectingCandidateStatus.Converting },
            candidate => candidate.BasicId == candidateId && candidate.Status == CrmProspectingCandidateStatus.Approved,
            cancellationToken);
    }
}
