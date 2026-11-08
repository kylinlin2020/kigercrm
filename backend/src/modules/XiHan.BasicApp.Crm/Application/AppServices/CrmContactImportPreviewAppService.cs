using System.Globalization;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Importing;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>Side-effect-free, permission-checked contact CSV preview.</summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Contact Import")]
public sealed class CrmContactImportPreviewAppService(
    ICrmCustomerLookupRepository customerLookupRepository)
    : CrmApplicationService, ICrmContactImportPreviewAppService
{
    private const int PublicPreviewLimit = 100;
    private static readonly string[] AllowedFields = ["CustomerId", "FirstName", "LastName", "JobTitle", "Email", "Phone", "WhatsApp", "Notes"];
    private static readonly string[] RequiredFields = ["CustomerId", "FirstName", "LastName"];

    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmCsvImportPreviewResult> PreviewAsync(
        CrmCsvImportPreviewRequestDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        // Parse every bounded row so relationship errors affect aggregate counts even when
        // the API returns only the first 100 preview rows.
        var parsed = CrmCsvImportPreviewService.Preview(
            input.FileBytes,
            AllowedFields,
            RequiredFields,
            input.FieldMapping,
            previewLimit: CrmCsvImportPreviewService.MaxRows,
            fieldValidators: new Dictionary<string, Func<string, bool>>(StringComparer.OrdinalIgnoreCase)
            {
                ["CustomerId"] = value =>
                    long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var customerId) &&
                    customerId > 0
            });

        var customerIds = parsed.PreviewRows
            .Select(row => row.Values.TryGetValue("CustomerId", out var value) &&
                long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var customerId) &&
                customerId > 0
                    ? (long?)customerId
                    : null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        var existingCustomerIds = await customerLookupRepository.GetExistingIdsAsync(
            customerIds,
            cancellationToken);

        var validatedRows = parsed.PreviewRows.Select(row =>
        {
            if (!row.Values.TryGetValue("CustomerId", out var rawId) ||
                !long.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out var customerId) ||
                customerId <= 0 ||
                existingCustomerIds.Contains(customerId))
                return row;

            return row with
            {
                Errors = row.Errors
                    .Append($"CustomerId {customerId} 不存在于当前租户。")
                    .ToArray()
            };
        }).ToArray();

        var invalidRows = validatedRows.Count(row => row.Errors.Count > 0);
        var validRows = validatedRows.Length - invalidRows;

        return parsed with
        {
            InvalidRows = invalidRows,
            ValidRows = validRows,
            PreviewRows = validatedRows.Take(PublicPreviewLimit).ToArray()
        };
    }
}
