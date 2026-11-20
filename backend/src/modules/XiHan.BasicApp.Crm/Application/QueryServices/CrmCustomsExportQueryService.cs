using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

/// <summary>
/// Deterministic customs-data utility. The operator supplies rows; this service validates,
/// filters and renders CSV. It performs no writes and queries no external customs data source.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "CustomsExport")]
public sealed class CrmCustomsExportQueryService : CrmApplicationService, ICrmCustomsExportQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public Task<CrmCustomsValidationDto> ValidateAsync(
        CrmCustomsInputDto input,
        CancellationToken cancellationToken = default)
    {
        var parsed = CrmCustomsExportPolicy.ParseRows(RequireJson(input));
        var result = new CrmCustomsValidationDto
        {
            Total = parsed.Count,
            Valid = parsed.Count(row => row.Errors.Count == 0),
            Rows = parsed.Select(row => new CrmCustomsValidationRowDto
            {
                CompanyName = row.CompanyName,
                HsCode = row.NormalizedHsCode,
                Errors = row.Errors
            }).ToArray()
        };
        return Task.FromResult(result);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public Task<CrmCustomsFilterResultDto> FilterAsync(
        CrmCustomsInputDto input,
        CancellationToken cancellationToken = default)
    {
        var parsed = CrmCustomsExportPolicy.ParseRows(RequireJson(input));
        var matched = CrmCustomsExportPolicy.Filter(parsed, ToFilter(input.Filter));
        var result = new CrmCustomsFilterResultDto
        {
            Total = parsed.Count,
            Matched = matched.Count,
            Rows = matched.Select(ToDto).ToArray()
        };
        return Task.FromResult(result);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public Task<CrmCustomsExportResultDto> ExportAsync(
        CrmCustomsInputDto input,
        CancellationToken cancellationToken = default)
    {
        var parsed = CrmCustomsExportPolicy.ParseRows(RequireJson(input));
        var matched = CrmCustomsExportPolicy.Filter(parsed, ToFilter(input.Filter));
        var result = new CrmCustomsExportResultDto
        {
            Exported = matched.Count,
            Csv = CrmCustomsExportPolicy.ToCsv(matched)
        };
        return Task.FromResult(result);
    }

    private static string RequireJson(CrmCustomsInputDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RowsJson);
        return input.RowsJson;
    }

    private static CrmCustomsFilter ToFilter(CrmCustomsFilterDto? input)
        => new(input?.Industry, input?.HsPrefix, input?.CountryCode);

    private static CrmCustomsRowDto ToDto(CrmCustomsRow row) => new()
    {
        CompanyName = row.CompanyName,
        CountryCode = row.NormalizedCountry,
        HsCode = row.NormalizedHsCode,
        Industry = row.NormalizedIndustry,
        ExportDate = row.ExportDate,
        Amount = row.Amount,
        ProductDescription = row.ProductDescription
    };
}