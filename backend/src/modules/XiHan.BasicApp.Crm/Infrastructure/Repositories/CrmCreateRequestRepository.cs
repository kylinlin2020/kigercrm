using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>Uses the standard strict tenant-filtered query path; no cross-tenant bypass.</summary>
public sealed class CrmCreateRequestRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmCreateRequest>(clientResolver), ICrmCreateRequestRepository
{
    public async Task<SysCrmCreateRequest?> GetByRequestKeyAsync(
        string documentType,
        string requestKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = await CreateQueryable()
            .Where(request => request.DocumentType == documentType && request.RequestKey == requestKey)
            .ToListAsync(cancellationToken);
        return matches.SingleOrDefault();
    }
}
