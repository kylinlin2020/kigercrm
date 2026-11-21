using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Exam certificates: issue on pass, list, verify and revoke.</summary>
public interface ICrmExamCertificateAppService : IApplicationService
{
    Task<CrmExamCertificateDto> IssueAsync(long attemptId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmExamCertificateDto>> GetMineAsync(CancellationToken cancellationToken = default);
    Task<CrmExamCertificateVerdictDto> VerifyAsync(CrmExamCertificateVerifyDto input, CancellationToken cancellationToken = default);
    Task<CrmExamCertificateDto> RevokeAsync(CrmExamCertificateRevokeDto input, CancellationToken cancellationToken = default);
}
