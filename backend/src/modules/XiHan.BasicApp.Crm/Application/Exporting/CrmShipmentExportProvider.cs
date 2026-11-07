using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Application.Exporting;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Exporting;

/// <summary>shipment export through the existing permission-checked, tenant-scoped query service.</summary>
public sealed class CrmShipmentExportProvider : QueryServiceExportProviderBase<CrmShipmentPageQueryDto, CrmShipmentListItemDto>
{
    private readonly ICrmShipmentQueryService _queryService;

    public CrmShipmentExportProvider(ICrmShipmentQueryService queryService)
        => _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));

    public override string BusinessType => "crm.shipment";

    // Query service re-applies its existing authorization and data-scope checks during export.
    public override string RequiredPermission => CrmPermissionCodes.Read;

    protected override Task<PageResultDtoBase<CrmShipmentListItemDto>> QueryPageAsync(
        CrmShipmentPageQueryDto query,
        CancellationToken cancellationToken)
        => _queryService.GetPageAsync(query, cancellationToken);
}
