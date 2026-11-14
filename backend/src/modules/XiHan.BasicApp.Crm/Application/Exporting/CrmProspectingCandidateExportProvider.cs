using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Application.Exporting;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Exporting;

/// <summary>
/// CRM prospecting candidate export provider. Reuses the candidate page query service so
/// tenant scope, record authorization, filters and field masking remain identical to the
/// review queue it exports.
/// </summary>
public sealed class CrmProspectingCandidateExportProvider : QueryServiceExportProviderBase<CrmProspectingCandidatePageQueryDto, CrmProspectingCandidateDto>
{
    private readonly ICrmProspectingCandidateQueryService _queryService;

    public CrmProspectingCandidateExportProvider(ICrmProspectingCandidateQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    public override string BusinessType => "crm.prospecting.candidate";

    // The export button is bound to Execute, so task submission enforces the same permission
    // as the UI action; the query service still repeats its own Read authorization per page.
    public override string RequiredPermission => CrmPermissionCodes.Execute;

    protected override Task<PageResultDtoBase<CrmProspectingCandidateDto>> QueryPageAsync(
        CrmProspectingCandidatePageQueryDto query,
        CancellationToken cancellationToken)
        => _queryService.GetPageAsync(query, cancellationToken);
}
