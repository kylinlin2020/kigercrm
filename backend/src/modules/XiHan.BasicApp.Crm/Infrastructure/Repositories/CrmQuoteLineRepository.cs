using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>SqlSugar implementation; tenant and soft-delete filters come from SaasRepository.</summary>
public sealed class CrmQuoteLineRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmQuoteLine>(clientResolver), ICrmQuoteLineRepository
{
    public async Task<IReadOnlyList<SysCrmQuoteLine>> ListForQuoteAsync(long quoteId, CancellationToken cancellationToken = default)
    {
        if (quoteId <= 0) throw new ArgumentOutOfRangeException(nameof(quoteId));
        cancellationToken.ThrowIfCancellationRequested();
        return await CreateQueryable()
            .Where(line => line.QuoteId == quoteId)
            .OrderBy(line => line.SortOrder)
            .OrderBy(line => line.BasicId)
            .ToListAsync(cancellationToken);
    }
}
