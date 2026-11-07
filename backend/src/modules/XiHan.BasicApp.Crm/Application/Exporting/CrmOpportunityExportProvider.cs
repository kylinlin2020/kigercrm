using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Application.Exporting;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Exporting;

/// <summary>Opportunity export through the existing tenant- and field-secured query service.</summary>
public sealed class CrmOpportunityExportProvider : QueryServiceExportProviderBase<CrmOpportunityPageQueryDto, CrmOpportunityListItemDto>
{
    private readonly ICrmOpportunityQueryService _queryService;

    public CrmOpportunityExportProvider(ICrmOpportunityQueryService queryService)
        => _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));

    public override string BusinessType => "crm.opportunity";

    // Keep the same authorization boundary as the opportunity list until a dedicated export
    // permission is introduced together with permission catalog and UI enforcement.
    public override string RequiredPermission => CrmPermissionCodes.Execute;

    protected override Task<PageResultDtoBase<CrmOpportunityListItemDto>> QueryPageAsync(
        CrmOpportunityPageQueryDto query,
        CancellationToken cancellationToken)
        => _queryService.GetPageAsync(query, cancellationToken);
}
