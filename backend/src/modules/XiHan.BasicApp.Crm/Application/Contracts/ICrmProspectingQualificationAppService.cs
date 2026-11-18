using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Four bounded qualification steps with an explicit, human-review-only output.</summary>
public interface ICrmProspectingQualificationAppService : IApplicationService
{
    Task<CrmProspectingIdentityMatchDto?> ResolveIdentityAsync(string companyName, CancellationToken cancellationToken = default);
    Task<CrmProspectingWebsiteAssessmentDto> AssessWebsiteAsync(CrmProspectingWebsiteAssessmentInputDto input, CancellationToken cancellationToken = default);
    Task<CrmProspectingVerificationReportDto> BuildVerificationReportAsync(CrmProspectingVerificationBuildDto input, CancellationToken cancellationToken = default);
    Task<CrmProspectingVerificationReportDto?> GetVerificationReportAsync(long candidateId, CancellationToken cancellationToken = default);
    Task<CrmProspectingIdentityBootstrapDto> BootstrapIdentityAsync(string companyName, string? countryCode = null, CancellationToken cancellationToken = default);
    Task<CrmProspectingPageFetchDto> ProbeWebsiteAsync(CrmProspectingWebsiteProbeInputDto input, CancellationToken cancellationToken = default);
}
