using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Importing;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>
/// Exposes a permission-checked, side-effect-free lead CSV preview. It deliberately does not
/// create import jobs or write leads; a separate confirmed import command is required for that.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Lead Import")]
public sealed class CrmLeadImportPreviewAppService : CrmApplicationService, ICrmLeadImportPreviewAppService
{
    private static readonly string[] AllowedFields =
    [
        "CompanyName",
        "ContactName",
        "Email",
        "Phone",
        "Website",
        "CountryRegion",
        "Source",
        "SourceReference",
        "Notes"
    ];

    private static readonly string[] RequiredFields = ["CompanyName"];

    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public Task<CrmCsvImportPreviewResult> PreviewAsync(
        CrmLeadImportPreviewRequestDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        // The client controls only source-header mapping. The target allowlist and required
        // fields remain server-owned so internal normalization and audit properties cannot be set.
        var result = CrmCsvImportPreviewService.Preview(
            input.FileBytes,
            AllowedFields,
            RequiredFields,
            input.FieldMapping,
            previewLimit: 100);

        return Task.FromResult(result);
    }
}
