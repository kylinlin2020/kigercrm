using System.Globalization;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Importing;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>Side-effect-free, permission-checked contact CSV preview.</summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Contact Import")]
public sealed class CrmContactImportPreviewAppService : CrmApplicationService, ICrmContactImportPreviewAppService
{
    private static readonly string[] AllowedFields = ["CustomerId", "FirstName", "LastName", "JobTitle", "Email", "Phone", "WhatsApp", "Notes"];
    private static readonly string[] RequiredFields = ["CustomerId", "FirstName", "LastName"];

    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public Task<CrmCsvImportPreviewResult> PreviewAsync(CrmCsvImportPreviewRequestDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CrmCsvImportPreviewService.Preview(
            input.FileBytes,
            AllowedFields,
            RequiredFields,
            input.FieldMapping,
            previewLimit: 100,
            fieldValidators: new Dictionary<string, Func<string, bool>>(StringComparer.OrdinalIgnoreCase)
            {
                ["CustomerId"] = value =>
                    long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var customerId) &&
                    customerId > 0
            }));
    }
}
