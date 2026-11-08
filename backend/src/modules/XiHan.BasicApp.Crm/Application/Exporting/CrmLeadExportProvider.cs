using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Application.Exporting;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Exporting;

/// <summary>
/// CRM lead export provider. Reuses the lead query service so tenant scope,
/// owner data scope, query guards, and field masking remain identical to the list view.
/// </summary>
public sealed class CrmLeadExportProvider : QueryServiceExportProviderBase<CrmLeadPageQueryDto, CrmLeadListItemDto>
{
    private readonly ICrmLeadQueryService _queryService;

    public CrmLeadExportProvider(ICrmLeadQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    public override string BusinessType => "crm.lead";

    // The existing CRM read permission is the authoritative permission for lead rows.
    // The query service repeats its own permission and field/data-scope checks at execution time.
    public override string RequiredPermission => CrmPermissionCodes.Execute;

    protected override Task<PageResultDtoBase<CrmLeadListItemDto>> QueryPageAsync(
        CrmLeadPageQueryDto query,
        CancellationToken cancellationToken)
        => _queryService.GetPageAsync(query, cancellationToken);
}
