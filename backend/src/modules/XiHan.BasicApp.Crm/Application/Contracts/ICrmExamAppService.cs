using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Sales training exams: question bank, published papers and bounded attempts.</summary>
public interface ICrmExamAppService : IApplicationService
{
    Task<CrmExamQuestionDto> CreateQuestionAsync(CrmExamQuestionCreateDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmExamQuestionDto>> GetQuestionsAsync(CancellationToken cancellationToken = default);
    Task<CrmExamPaperDto> CreatePaperAsync(CrmExamPaperCreateDto input, CancellationToken cancellationToken = default);
    Task<CrmExamPaperDto> PublishPaperAsync(long paperId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmExamPaperDto>> GetPapersAsync(CancellationToken cancellationToken = default);
    Task<CrmExamAttemptDto> StartAsync(CrmExamStartDto input, CancellationToken cancellationToken = default);
    Task<CrmExamAttemptDto> SubmitAsync(CrmExamSubmissionDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmExamAttemptDto>> GetMyAttemptsAsync(CancellationToken cancellationToken = default);
}
