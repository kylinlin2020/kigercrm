using XiHan.BasicApp.AI.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.AI.Application.Contracts;

/// <summary>Knowledge document tagging and byte-identical duplicate detection.</summary>
public interface IKnowledgeTagAppService : IApplicationService
{
    Task<KnowledgeTagItemDto[]> SetTagsAsync(KnowledgeTagsUpdateDto input, CancellationToken cancellationToken = default);
    Task<KnowledgeTagItemDto[]> GetTagsAsync(CancellationToken cancellationToken = default);
    Task<KnowledgeDuplicateReportDto> GetDuplicatesAsync(CancellationToken cancellationToken = default);
}
