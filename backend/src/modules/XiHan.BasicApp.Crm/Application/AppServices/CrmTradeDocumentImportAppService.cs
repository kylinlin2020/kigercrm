using System.Text.Json;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
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
/// Trade-document OCR import. Preview is pure validation, import stages an auditable batch, and
/// confirmation delegates to the existing shipment creation contract with operator-mapped lines.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "TradeDocumentImport")]
public sealed class CrmTradeDocumentImportAppService(
    ICrmTradeDocumentImportRepository rows,
    ICrmSalesOrderLineRepository orderLines,
    ICrmShipmentAppService shipments)
    : CrmApplicationService, ICrmTradeDocumentImportAppService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmTradeDocumentPreviewDto> PreviewAsync(
        CrmTradeDocumentImportInputDto input,
        CancellationToken cancellationToken = default)
    {
        var parsed = CrmTradeDocumentPolicy.ParseRows(RequireJson(input));
        var duplicates = await BuildDuplicateMapAsync(parsed, cancellationToken);
        var suggestions = await BuildSuggestionsAsync(parsed, input.SalesOrderId, cancellationToken);
        var result = new CrmTradeDocumentPreviewDto
        {
            Total = parsed.Count,
            Valid = parsed.Count(item => item.Errors.Count == 0),
            Duplicates = duplicates.Count
        };
        result.Rows = parsed.Select(item => ToPreviewRow(item, duplicates, suggestions)).ToArray();
        return result;
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmTradeDocumentImportResultDto> ImportAsync(
        CrmTradeDocumentImportInputDto input,
        CancellationToken cancellationToken = default)
    {
        var parsed = CrmTradeDocumentPolicy.ParseRows(RequireJson(input));
        var batchId = string.IsNullOrWhiteSpace(input.BatchId)
            ? $"trade-{Guid.NewGuid():N}"
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
                continue;
            }
            await rows.AddAsync(new SysCrmTradeDocumentImportRow
            {
                BatchId = batchId,
                ExternalId = item.ExternalId,
                DocumentType = item.DocumentType,
                DocumentNumber = item.DocumentNumber,
                OrderReference = item.OrderReference,
                Buyer = item.Buyer,
                Seller = item.Seller,
                Carrier = item.Carrier,
                TrackingNumber = item.TrackingNumber,
                ShipmentDate = item.ShipmentDate,
                Currency = item.Currency,
                TotalAmount = item.TotalAmount,
                LinesJson = JsonSerializer.Serialize(item.Lines, JsonOptions),
                RawOcrText = item.RawOcrText,
                NormalizedDocumentKey = item.NormalizedDocumentKey,
                Status = CrmTradeDocumentImportStatus.Pending,
                DuplicateReason = duplicates.GetValueOrDefault(item.ExternalId)
            }, cancellationToken);
            staged++;
            if (duplicates.ContainsKey(item.ExternalId)) duplicateStaged++;
        }
        return new CrmTradeDocumentImportResultDto
        {
            BatchId = batchId,
            Staged = staged,
            Invalid = invalid,
            DuplicateStaged = duplicateStaged
        };
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmTradeDocumentRowDto> ConfirmAsync(
        CrmTradeDocumentConfirmDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.SalesOrderId <= 0) throw new ArgumentOutOfRangeException(nameof(input.SalesOrderId));
        var row = await rows.GetByIdAsync(input.RowId, cancellationToken)
            ?? throw new InvalidOperationException("Trade-document import row does not exist.");
        var gate = CrmTradeDocumentPolicy.CanConfirm(row.Status, row.DuplicateReason);
        if (!gate.Allowed) throw new InvalidOperationException($"Trade document cannot be confirmed: {gate.Reason}.");

        var orderRowLines = await orderLines.ListForOrderAsync(input.SalesOrderId, cancellationToken);
        var extracted = JsonSerializer.Deserialize<CrmTradeDocumentParsedLine[]>(row.LinesJson, JsonOptions) ?? [];
        var mapped = input.Lines.Select(line => (line.OrderLineId, line.Quantity)).ToArray();
        CrmTradeDocumentPolicy.ValidateMapping(extracted, orderRowLines, mapped);

        var shipment = await shipments.CreateAsync(new CrmShipmentCreateDto
        {
            ShipmentNumber = row.DocumentNumber,
            SalesOrderId = input.SalesOrderId,
            ShipmentDate = row.ShipmentDate,
            Carrier = row.Carrier,
            TrackingNumber = row.TrackingNumber,
            Notes = $"OCR import {row.BatchId}:{row.ExternalId}",
            Lines = input.Lines.Select(line => new CrmShipmentLineCreateDto
            {
                SalesOrderLineId = line.OrderLineId,
                Quantity = line.Quantity
            }).ToList()
        }, cancellationToken);

        row.Status = CrmTradeDocumentImportStatus.Confirmed;
        row.SalesOrderId = input.SalesOrderId;
        row.MappedLinesJson = JsonSerializer.Serialize(input.Lines, JsonOptions);
        row.CreatedShipmentId = shipment.BasicId;
        return ToDto(await rows.UpdateAsync(row, cancellationToken));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmTradeDocumentRowDto> RejectAsync(
        CrmTradeDocumentDecisionDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = await rows.GetByIdAsync(input.RowId, cancellationToken)
            ?? throw new InvalidOperationException("Trade-document import row does not exist.");
        var gate = CrmTradeDocumentPolicy.CanReject(row.Status, input.Reason);
        if (!gate.Allowed) throw new InvalidOperationException($"Trade document cannot be rejected: {gate.Reason}.");
        row.Status = CrmTradeDocumentImportStatus.Rejected;
        row.RejectReason = input.Reason!.Trim();
        return ToDto(await rows.UpdateAsync(row, cancellationToken));
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmTradeDocumentRowDto>> GetRowsAsync(
        string batchId,
        CancellationToken cancellationToken = default)
        => (await rows.GetByBatchAsync(batchId, cancellationToken)).Select(ToDto).ToArray();

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public Task<IReadOnlyList<string>> GetBatchesAsync(CancellationToken cancellationToken = default)
        => rows.GetBatchIdsAsync(cancellationToken);

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmTradeDocumentOrderLineDto>> GetOrderLinesAsync(
        long salesOrderId,
        CancellationToken cancellationToken = default)
    {
        if (salesOrderId <= 0) throw new ArgumentOutOfRangeException(nameof(salesOrderId));
        var lines = await orderLines.ListForOrderAsync(salesOrderId, cancellationToken);
        return lines.Select(line => new CrmTradeDocumentOrderLineDto
        {
            BasicId = line.BasicId,
            ProductCode = line.ProductCode,
            Description = line.Description,
            AvailableQuantity = line.Quantity - line.ReservedQuantity - line.ShippedQuantity
        }).ToArray();
    }

    private static string RequireJson(CrmTradeDocumentImportInputDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RowsJson);
        return input.RowsJson;
    }

    private async Task<Dictionary<string, string>> BuildDuplicateMapAsync(
        IReadOnlyList<CrmTradeDocumentParsedRow> parsed,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in parsed.Where(item => item.Errors.Count == 0))
        {
            if (seen.TryGetValue(item.NormalizedDocumentKey, out var first))
                result.TryAdd(item.ExternalId, $"duplicate_in_batch:{first}");
            else seen[item.NormalizedDocumentKey] = item.ExternalId;
        }
        var keys = parsed.Where(item => item.Errors.Count == 0)
            .Select(item => item.NormalizedDocumentKey).ToArray();
        var existing = await rows.GetByDocumentKeysAsync(keys, cancellationToken);
        var existingKeys = existing.Select(item => item.NormalizedDocumentKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in parsed.Where(item => item.Errors.Count == 0))
        {
            if (existingKeys.Contains(item.NormalizedDocumentKey))
                result.TryAdd(item.ExternalId, "duplicate_existing_document");
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<string, long>> BuildSuggestionsAsync(
        IReadOnlyList<CrmTradeDocumentParsedRow> parsed,
        long? salesOrderId,
        CancellationToken cancellationToken)
    {
        if (salesOrderId is not > 0) return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var orderRowLines = await orderLines.ListForOrderAsync(salesOrderId.Value, cancellationToken);
        var suggestions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in parsed)
        {
            foreach (var (sku, lineId) in CrmTradeDocumentPolicy.SuggestLines(item.Lines, orderRowLines))
                suggestions.TryAdd(sku, lineId);
        }
        return suggestions;
    }

    private static CrmTradeDocumentPreviewRowDto ToPreviewRow(
        CrmTradeDocumentParsedRow item,
        IReadOnlyDictionary<string, string> duplicates,
        IReadOnlyDictionary<string, long> suggestions) => new()
    {
        ExternalId = item.ExternalId,
        DocumentType = (int)item.DocumentType,
        DocumentNumber = item.DocumentNumber,
        OrderReference = item.OrderReference,
        Buyer = item.Buyer,
        Seller = item.Seller,
        Carrier = item.Carrier,
        TrackingNumber = item.TrackingNumber,
        ShipmentDate = item.ShipmentDate,
        Currency = item.Currency,
        TotalAmount = item.TotalAmount,
        Lines = item.Lines.Select(line => new CrmTradeDocumentLinePreviewDto
        {
            Sku = line.Sku,
            Quantity = line.Quantity,
            Description = line.Description,
            SuggestedOrderLineId = suggestions.TryGetValue(line.Sku, out var id) ? id : null
        }).ToArray(),
        Errors = item.Errors,
        DuplicateReason = duplicates.GetValueOrDefault(item.ExternalId)
    };

    private static CrmTradeDocumentRowDto ToDto(SysCrmTradeDocumentImportRow row) => new()
    {
        BasicId = row.BasicId,
        BatchId = row.BatchId,
        ExternalId = row.ExternalId,
        DocumentType = (int)row.DocumentType,
        DocumentNumber = row.DocumentNumber,
        OrderReference = row.OrderReference,
        Carrier = row.Carrier,
        TrackingNumber = row.TrackingNumber,
        ShipmentDate = row.ShipmentDate,
        Currency = row.Currency,
        TotalAmount = row.TotalAmount,
        Lines = (JsonSerializer.Deserialize<CrmTradeDocumentParsedLine[]>(row.LinesJson, JsonOptions) ?? [])
            .Select(line => new CrmTradeDocumentLineDto
            {
                Sku = line.Sku,
                Quantity = line.Quantity,
                Description = line.Description
            }).ToArray(),
        Status = (int)row.Status,
        DuplicateReason = row.DuplicateReason,
        RejectReason = row.RejectReason,
        CreatedShipmentId = row.CreatedShipmentId
    };
}