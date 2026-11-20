using System.Text.Json;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.Contracts;
using XiHan.BasicApp.Saas.Application.Dtos;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>
/// CRM customer import. Preview is pure validation with zero writes; import creates only valid,
/// non-duplicate rows through the Customer domain service and records an ImportHistory audit entry.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "CustomerImport")]
public sealed class CrmCustomerImportAppService(
    ICrmCustomerRepository customers,
    ICrmCustomerDomainService customerDomain,
    IImportHistoryAppService importHistory)
    : CrmApplicationService, ICrmCustomerImportAppService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmCustomerImportPreviewDto> PreviewAsync(
        CrmCustomerImportInputDto input,
        CancellationToken cancellationToken = default)
    {
        var parsed = Parse(input);
        var duplicates = await BuildDuplicateMapAsync(parsed, cancellationToken);
        return new CrmCustomerImportPreviewDto
        {
            Total = parsed.Count,
            Valid = parsed.Count(row => row.Errors.Count == 0),
            Duplicates = duplicates.Count,
            Rows = parsed.Select(row => new CrmCustomerImportPreviewRowDto
            {
                ExternalId = row.ExternalId,
                Name = row.Name,
                Website = row.Website,
                CountryRegion = row.CountryRegion,
                Industry = row.Industry,
                Errors = row.Errors,
                DuplicateReason = duplicates.GetValueOrDefault(row.ExternalId)
            }).ToArray()
        };
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmCustomerImportResultDto> ImportAsync(
        CrmCustomerImportInputDto input,
        CancellationToken cancellationToken = default)
    {
        var parsed = Parse(input);
        var duplicates = await BuildDuplicateMapAsync(parsed, cancellationToken);
        var createdIds = new List<long>();
        var invalid = 0;
        var duplicateCount = 0;
        var errors = new List<string>();
        foreach (var row in parsed)
        {
            if (row.Errors.Count > 0)
            {
                invalid++;
                errors.Add($"{row.ExternalId}: {string.Join(',', row.Errors)}");
                continue;
            }
            if (duplicates.ContainsKey(row.ExternalId))
            {
                duplicateCount++;
                errors.Add($"{row.ExternalId}: {duplicates[row.ExternalId]}");
                continue;
            }
            var created = await customerDomain.CreateAsync(new CrmCustomerCreateCommand(
                row.Name,
                row.LegalName,
                row.Website,
                row.CountryRegion,
                row.Industry,
                row.Source,
                null,
                row.Notes), cancellationToken);
            createdIds.Add(created.BasicId);
        }

        var history = await importHistory.CreateAsync(new ImportHistoryCreateDto
        {
            PageCode = "crm.customer-import",
            ResourceCode = "CrmCustomer",
            FileName = string.IsNullOrWhiteSpace(input.FileName) ? "customer-import.json" : input.FileName.Trim(),
            TotalCount = parsed.Count,
            SuccessCount = createdIds.Count,
            FailCount = invalid + duplicateCount,
            ErrorSummary = errors.Count == 0 ? null : JsonSerializer.Serialize(errors)
        }, cancellationToken);

        return new CrmCustomerImportResultDto
        {
            Total = parsed.Count,
            Created = createdIds.Count,
            Invalid = invalid,
            Duplicates = duplicateCount,
            CreatedCustomerIds = createdIds,
            ImportHistoryId = history.BasicId
        };
    }

    private static IReadOnlyList<CrmCustomerImportRow> Parse(CrmCustomerImportInputDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return CrmCustomerImportPolicy.ParseRows(input.RowsJson, input.Mapping);
    }

    private async Task<Dictionary<string, string>> BuildDuplicateMapAsync(
        IReadOnlyList<CrmCustomerImportRow> parsed,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var seenNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in parsed.Where(row => row.Errors.Count == 0))
        {
            if (seenNames.TryGetValue(row.NameKey, out var first))
                result.TryAdd(row.ExternalId, $"duplicate_in_batch:{first}");
            else seenNames[row.NameKey] = row.ExternalId;
        }
        var keys = parsed.Where(row => row.Errors.Count == 0)
            .Select(row => row.NameKey.ToLowerInvariant()).ToArray();
        var existing = await customers.GetByNameKeysAsync(keys, cancellationToken);
        var existingNames = existing.Select(item => CrmCustomerImportPolicy.NormalizeName(item.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in parsed.Where(row => row.Errors.Count == 0))
        {
            if (existingNames.Contains(row.NameKey))
                result.TryAdd(row.ExternalId, "duplicate_existing_customer");
        }
        return result;
    }
}