using XiHan.BasicApp.AI.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.AI.Application.Contracts;

/// <summary>Application-layer knowledge access control: visibility and role allowlist.</summary>
public interface IKnowledgeAccessAppService : IApplicationService
{
    Task<KnowledgeAccessDto> UpdateAsync(KnowledgeAccessUpdateDto input, CancellationToken cancellationToken = default);
    Task<KnowledgeAccessDto> GetAsync(long documentId, CancellationToken cancellationToken = default);
}
