using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Importing;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>Side-effect-free, permission-checked customer CSV preview.</summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Customer Import")]
public sealed class CrmCustomerImportPreviewAppService : CrmApplicationService, ICrmCustomerImportPreviewAppService
{
    private static readonly string[] AllowedFields = ["Name", "LegalName", "Website", "CountryRegion", "Industry", "Source", "Notes", "ExternalSystem", "ExternalId"];
    private static readonly string[] RequiredFields = ["Name"];

    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public Task<CrmCsvImportPreviewResult> PreviewAsync(CrmLeadImportPreviewRequestDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CrmCsvImportPreviewService.Preview(input.FileBytes, AllowedFields, RequiredFields, input.FieldMapping, 100));
    }
}
