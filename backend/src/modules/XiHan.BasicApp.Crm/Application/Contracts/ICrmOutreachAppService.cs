using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Human-gated outreach sequence, enrollment and effect-review operations.</summary>
public interface ICrmOutreachAppService : IApplicationService
{
    Task<CrmOutreachSequenceDto> CreateSequenceAsync(CrmOutreachSequenceCreateDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmOutreachSequenceDto>> GetSequencesAsync(CancellationToken cancellationToken = default);
    Task<CrmOutreachSequenceDto> ActivateSequenceAsync(long sequenceId, CancellationToken cancellationToken = default);
    Task<CrmOutreachSequenceDto> PauseSequenceAsync(long sequenceId, CancellationToken cancellationToken = default);
    Task<CrmOutreachEnrollmentDto> EnrollCandidateAsync(CrmOutreachEnrollDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmOutreachEnrollmentDto>> GetEnrollmentsAsync(long sequenceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmOutreachTouchpointDto>> GetTouchpointsAsync(long enrollmentId, CancellationToken cancellationToken = default);
    Task<CrmOutreachTouchpointDto> ApproveTouchpointAsync(long touchpointId, CancellationToken cancellationToken = default);
    Task<CrmOutreachTouchpointDto> RecordTouchpointAsync(CrmOutreachRecordDto input, CancellationToken cancellationToken = default);
    Task<CrmOutreachEnrollmentDto> SuppressEnrollmentAsync(long enrollmentId, string reason, CancellationToken cancellationToken = default);
    Task<CrmOutreachReviewDto> GetReviewAsync(long sequenceId, CancellationToken cancellationToken = default);
}
