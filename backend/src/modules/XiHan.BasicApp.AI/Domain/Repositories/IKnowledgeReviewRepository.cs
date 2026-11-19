using XiHan.BasicApp.AI.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.AI.Domain.Repositories;

public interface IKnowledgeReviewRepository : ISaasRepository<SysKnowledgeDocumentReview>
{
    Task<SysKnowledgeDocumentReview?> GetLatestByDocumentAsync(long documentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysKnowledgeDocumentReview>> GetByDocumentAsync(long documentId, CancellationToken cancellationToken = default);
}
