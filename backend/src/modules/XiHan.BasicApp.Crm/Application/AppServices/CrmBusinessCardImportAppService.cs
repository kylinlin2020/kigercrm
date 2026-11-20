using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>
/// Business-card OCR import. Preview is pure validation, import stages an auditable batch, and only
/// an explicit operator confirmation creates a Lead through the existing Lead domain service.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "BusinessCardImport")]
public sealed class CrmBusinessCardImportAppService(
    ICrmBusinessCardImportRepository rows,
    ICrmLeadRepository leads,
    ICrmLeadDomainService leadDomain)
    : CrmApplicationService, ICrmBusinessCardImportAppService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmBusinessCardPreviewDto> PreviewAsync(
        CrmBusinessCardImportInputDto input,
        CancellationToken cancellationToken = default)
    {
        var parsed = CrmBusinessCardPolicy.ParseRows(RequireJson(input));
        var duplicates = await BuildDuplicateMapAsync(parsed, cancellationToken);
        var result = new CrmBusinessCardPreviewDto
        {
            Total = parsed.Count,
            Valid = parsed.Count(item => item.Errors.Count == 0),
            Duplicates = duplicates.Count
        };
        result.Rows = parsed.Select(item => ToPreviewRow(item, duplicates)).ToArray();
        return result;
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmBusinessCardImportResultDto> ImportAsync(
        CrmBusinessCardImportInputDto input,
        CancellationToken cancellationToken = default)
    {
        var parsed = CrmBusinessCardPolicy.ParseRows(RequireJson(input));
        var batchId = string.IsNullOrWhiteSpace(input.BatchId)
            ? $"card-{Guid.NewGuid():N}"
            : input.BatchId.Trim();
        var duplicates = await BuildDuplicateMapAsync(parsed, cancellationToken);
        var staged = 0;
        var invalid = 0;
        var duplicateStaged = 0;
        foreach (var item in parsed)
        {
            if (item.Errors.Count > 0)
            {
                invalid++;
                continue; // Invalid rows are reported, never silently staged.
            }
            var duplicateReason = duplicates.GetValueOrDefault(item.ExternalId);
            await rows.AddAsync(new SysCrmBusinessCardImportRow
            {
                BatchId = batchId,
                ExternalId = item.ExternalId,
                ContactName = item.ContactName,
                CompanyName = item.CompanyName,
                Phone = item.Phone,
                Email = item.Email,
                JobTitle = item.JobTitle,
                Website = item.Website,
                RawOcrText = item.RawOcrText,
                NormalizedEmail = item.NormalizedEmail,
                PhoneDigits = item.PhoneDigits,
                WebsiteHost = item.WebsiteHost,
                Status = CrmBusinessCardImportStatus.Pending,
                DuplicateReason = duplicateReason
            }, cancellationToken);
            staged++;
            if (duplicateReason is not null) duplicateStaged++;
        }
        return new CrmBusinessCardImportResultDto
        {
            BatchId = batchId,
            Staged = staged,
            Invalid = invalid,
            DuplicateStaged = duplicateStaged
        };
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmBusinessCardRowDto> ConfirmAsync(
        CrmBusinessCardDecisionDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = await rows.GetByIdAsync(input.RowId, cancellationToken)
            ?? throw new InvalidOperationException("Business-card import row does not exist.");
        var gate = CrmBusinessCardPolicy.CanConfirm(row.Status, row.DuplicateReason);
        if (!gate.Allowed) throw new InvalidOperationException($"Business card cannot be confirmed: {gate.Reason}.");
        var lead = await leadDomain.CreateAsync(new CrmLeadCreateCommand(
            row.CompanyName ?? row.ContactName ?? "Unknown company",
            row.ContactName,
            row.Email,
            row.Phone,
            row.Website,
            null,
            "BusinessCardOcr",
            $"{row.BatchId}:{row.ExternalId}",
            null,
            row.JobTitle), cancellationToken);
        row.Status = CrmBusinessCardImportStatus.Confirmed;
        row.ConvertedLeadId = lead.BasicId;
        return ToDto(await rows.UpdateAsync(row, cancellationToken));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmBusinessCardRowDto> RejectAsync(
        CrmBusinessCardDecisionDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = await rows.GetByIdAsync(input.RowId, cancellationToken)
            ?? throw new InvalidOperationException("Business-card import row does not exist.");
        var gate = CrmBusinessCardPolicy.CanReject(row.Status, input.Reason);
        if (!gate.Allowed) throw new InvalidOperationException($"Business card cannot be rejected: {gate.Reason}.");
        row.Status = CrmBusinessCardImportStatus.Rejected;
        row.RejectReason = input.Reason!.Trim();
        return ToDto(await rows.UpdateAsync(row, cancellationToken));
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmBusinessCardRowDto>> GetRowsAsync(
        string batchId,
        CancellationToken cancellationToken = default)
        => (await rows.GetByBatchAsync(batchId, cancellationToken)).Select(ToDto).ToArray();

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public Task<IReadOnlyList<string>> GetBatchesAsync(CancellationToken cancellationToken = default)
        => rows.GetBatchIdsAsync(cancellationToken);

    private static string RequireJson(CrmBusinessCardImportInputDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RowsJson);
        return input.RowsJson;
    }

    private async Task<Dictionary<string, string>> BuildDuplicateMapAsync(
        IReadOnlyList<CrmBusinessCardParsedRow> parsed,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var seenEmails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenPhones = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in parsed.Where(item => item.Errors.Count == 0))
        {
            if (item.NormalizedEmail is not null)
            {
                if (seenEmails.TryGetValue(item.NormalizedEmail, out var firstEmail))
                    result.TryAdd(item.ExternalId, $"duplicate_in_batch_email:{firstEmail}");
                else seenEmails[item.NormalizedEmail] = item.ExternalId;
            }
            if (item.PhoneDigits is not null)
            {
                if (seenPhones.TryGetValue(item.PhoneDigits, out var firstPhone))
                    result.TryAdd(item.ExternalId, $"duplicate_in_batch_phone:{firstPhone}");
                else seenPhones[item.PhoneDigits] = item.ExternalId;
            }
        }

        var emailKeys = parsed.Where(item => item.NormalizedEmail is not null)
            .Select(item => item.NormalizedEmail!).ToArray();
        var phoneKeys = parsed.Where(item => item.PhoneDigits is not null)
            .Select(item => item.PhoneDigits!).ToArray();
        var leadEmailMatches = await leads.GetDuplicateMatchesAsync(emailKeys, [], cancellationToken);
        var leadPhoneMatches = await leads.GetByPhoneDigitsAsync(phoneKeys, cancellationToken);
        var leadEmails = leadEmailMatches
            .Where(lead => lead.DuplicateEmailNormalized is not null)
            .Select(lead => lead.DuplicateEmailNormalized!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var leadPhones = leadPhoneMatches
            .Where(lead => lead.PhoneDigits is not null)
            .Select(lead => lead.PhoneDigits!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var item in parsed.Where(item => item.Errors.Count == 0))
        {
            if (item.NormalizedEmail is not null && leadEmails.Contains(item.NormalizedEmail))
                result.TryAdd(item.ExternalId, "duplicate_lead_email");
            if (item.PhoneDigits is not null && leadPhones.Contains(item.PhoneDigits))
                result.TryAdd(item.ExternalId, "duplicate_lead_phone");
        }
        return result;
    }

    private static CrmBusinessCardPreviewRowDto ToPreviewRow(
        CrmBusinessCardParsedRow item,
        IReadOnlyDictionary<string, string> duplicates) => new()
    {
        ExternalId = item.ExternalId,
        ContactName = item.ContactName,
        CompanyName = item.CompanyName,
        Phone = item.Phone,
        Email = item.Email,
        JobTitle = item.JobTitle,
        Website = item.Website,
        NormalizedEmail = item.NormalizedEmail,
        PhoneDigits = item.PhoneDigits,
        WebsiteHost = item.WebsiteHost,
        Errors = item.Errors,
        DuplicateReason = duplicates.GetValueOrDefault(item.ExternalId)
    };

    private static CrmBusinessCardRowDto ToDto(SysCrmBusinessCardImportRow row) => new()
    {
        BasicId = row.BasicId,
        BatchId = row.BatchId,
        ExternalId = row.ExternalId,
        ContactName = row.ContactName,
        CompanyName = row.CompanyName,
        Phone = row.Phone,
        Email = row.Email,
        JobTitle = row.JobTitle,
        Website = row.Website,
        Status = (int)row.Status,
        DuplicateReason = row.DuplicateReason,
        RejectReason = row.RejectReason,
        ConvertedLeadId = row.ConvertedLeadId
    };
}
