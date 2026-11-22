using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Human-gated outreach drafts: whitelist template interpolation and explicit review.</summary>
public interface ICrmOutreachDraftAppService : IApplicationService
{
    Task<string> PreviewAsync(CrmOutreachDraftPreviewDto input, CancellationToken cancellationToken = default);

    Task<CrmOutreachDraftDto> CreateAsync(CrmOutreachDraftCreateDto input, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CrmOutreachDraftDto>> ListAsync(long enrollmentId, CancellationToken cancellationToken = default);

    Task<CrmOutreachDraftDto?> GetDraftAsync(long draftId, CancellationToken cancellationToken = default);

    Task<CrmOutreachDraftDto> ApproveAsync(long draftId, CancellationToken cancellationToken = default);

    Task<CrmOutreachDraftDto> RejectAsync(CrmOutreachDraftReviewDto input, CancellationToken cancellationToken = default);
}
