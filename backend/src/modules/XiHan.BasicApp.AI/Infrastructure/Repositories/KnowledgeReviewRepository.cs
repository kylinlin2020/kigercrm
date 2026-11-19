using XiHan.BasicApp.AI.Domain.Entities;
using XiHan.BasicApp.AI.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.AI.Infrastructure.Repositories;

public sealed class KnowledgeReviewRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysKnowledgeDocumentReview>(clientResolver), IKnowledgeReviewRepository
{
    public async Task<SysKnowledgeDocumentReview?> GetLatestByDocumentAsync(long documentId, CancellationToken cancellationToken = default)
    {
        if (documentId <= 0) throw new ArgumentOutOfRangeException(nameof(documentId));
        var rows = await CreateQueryable().Where(item => item.DocumentId == documentId)
            .OrderByDescending(item => item.CreatedTime)
            .Take(1)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SysKnowledgeDocumentReview>> GetByDocumentAsync(long documentId, CancellationToken cancellationToken = default)
    {
        if (documentId <= 0) throw new ArgumentOutOfRangeException(nameof(documentId));
        return await CreateQueryable().Where(item => item.DocumentId == documentId)
            .OrderByDescending(item => item.CreatedTime)
            .Take(200)
            .ToListAsync(cancellationToken);
    }
}
