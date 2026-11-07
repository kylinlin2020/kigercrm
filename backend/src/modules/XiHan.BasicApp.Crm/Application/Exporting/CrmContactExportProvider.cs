using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Application.Exporting;
using XiHan.Framework.Domain.Shared.Paging.Dtos;

namespace XiHan.BasicApp.Crm.Application.Exporting;

/// <summary>Contact export through the existing tenant- and field-secured query service.</summary>
public sealed class CrmContactExportProvider : QueryServiceExportProviderBase<CrmContactPageQueryDto, CrmContactListItemDto>
{
    private readonly ICrmContactQueryService _queryService;

    public CrmContactExportProvider(ICrmContactQueryService queryService)
        => _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));

    public override string BusinessType => "crm.contact";
    public override string RequiredPermission => CrmPermissionCodes.Read;

    protected override Task<PageResultDtoBase<CrmContactListItemDto>> QueryPageAsync(
        CrmContactPageQueryDto query,
        CancellationToken cancellationToken)
        => _queryService.GetPageAsync(query, cancellationToken);
}
