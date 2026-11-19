using XiHan.BasicApp.AI.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.AI.Application.Contracts;

public interface IKnowledgeReviewAppService : IApplicationService
{
    Task<KnowledgeReviewDto> SubmitAsync(long documentId, CancellationToken cancellationToken = default);
    Task<KnowledgeReviewDto> DecideAsync(KnowledgeReviewDecisionDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeReviewDto>> GetReviewsAsync(long documentId, CancellationToken cancellationToken = default);
}
