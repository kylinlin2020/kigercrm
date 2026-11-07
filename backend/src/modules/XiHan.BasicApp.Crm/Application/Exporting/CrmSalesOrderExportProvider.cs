using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Application.Exporting;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Exporting;

/// <summary>sales order export through the existing permission-checked, tenant-scoped query service.</summary>
public sealed class CrmSalesOrderExportProvider : QueryServiceExportProviderBase<CrmSalesOrderPageQueryDto, CrmSalesOrderListItemDto>
{
    private readonly ICrmSalesOrderQueryService _queryService;

    public CrmSalesOrderExportProvider(ICrmSalesOrderQueryService queryService)
        => _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));

    public override string BusinessType => "crm.sales-order";

    // Query service re-applies its existing authorization and data-scope checks during export.
    public override string RequiredPermission => CrmPermissionCodes.Execute;

    protected override Task<PageResultDtoBase<CrmSalesOrderListItemDto>> QueryPageAsync(
        CrmSalesOrderPageQueryDto query,
        CancellationToken cancellationToken)
        => _queryService.GetPageAsync(query, cancellationToken);
}
