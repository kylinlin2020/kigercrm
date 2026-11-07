using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Application.Exporting;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Exporting;

/// <summary>Customer export through the existing tenant- and field-secured query service.</summary>
public sealed class CrmCustomerExportProvider : QueryServiceExportProviderBase<CrmCustomerPageQueryDto, CrmCustomerListItemDto>
{
    private readonly ICrmCustomerQueryService _queryService;

    public CrmCustomerExportProvider(ICrmCustomerQueryService queryService)
        => _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));

    public override string BusinessType => "crm.customer";
    public override string RequiredPermission => CrmPermissionCodes.Read;

    protected override Task<PageResultDtoBase<CrmCustomerListItemDto>> QueryPageAsync(
        CrmCustomerPageQueryDto query,
        CancellationToken cancellationToken)
        => _queryService.GetPageAsync(query, cancellationToken);
}
