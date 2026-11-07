using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Application.Exporting;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Exporting;

/// <summary>quote export through the existing permission-checked, tenant-scoped query service.</summary>
public sealed class CrmQuoteExportProvider : QueryServiceExportProviderBase<CrmQuotePageQueryDto, CrmQuoteListItemDto>
{
    private readonly ICrmQuoteQueryService _queryService;

    public CrmQuoteExportProvider(ICrmQuoteQueryService queryService)
        => _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));

    public override string BusinessType => "crm.quote";

    // Query service re-applies its existing authorization and data-scope checks during export.
    public override string RequiredPermission => CrmPermissionCodes.Read;

    protected override Task<PageResultDtoBase<CrmQuoteListItemDto>> QueryPageAsync(
        CrmQuotePageQueryDto query,
        CancellationToken cancellationToken)
        => _queryService.GetPageAsync(query, cancellationToken);
}
