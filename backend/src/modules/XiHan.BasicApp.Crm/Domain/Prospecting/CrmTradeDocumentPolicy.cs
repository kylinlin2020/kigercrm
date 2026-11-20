using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

public sealed record CrmTradeDocumentParsedLine(string Sku, decimal Quantity, string? Description);

public sealed record CrmTradeDocumentParsedRow(
    string ExternalId,
    CrmTradeDocumentType DocumentType,
    string DocumentNumber,
    string OrderReference,
    string? Buyer,
    string? Seller,
    string? Carrier,
    string? TrackingNumber,
    DateTime? ShipmentDate,
    string? Currency,
    decimal TotalAmount,
    IReadOnlyList<CrmTradeDocumentParsedLine> Lines,
    string? RawOcrText,
    IReadOnlyList<string> Errors)
{
    public string NormalizedDocumentKey
        => $"{(int)DocumentType}:{DocumentNumber.Trim().ToUpperInvariant()}";
}

/// <summary>
/// Deterministic trade-document field validation and line mapping suggestions. No OCR engine is
/// invoked; the operator supplies extracted fields and explicitly maps them to order lines.
/// </summary>
public static partial class CrmTradeDocumentPolicy
{
    public const int MaxRows = 100;

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyPattern();

    public static IReadOnlyList<CrmTradeDocumentParsedRow> ParseRows(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Trade-document OCR payload must be a JSON array.", nameof(json));
        var elements = document.RootElement.EnumerateArray().ToArray();
        if (elements.Length is < 1 or > MaxRows)
            throw new ArgumentException($"Trade-document OCR payload must contain 1..{MaxRows} rows.", nameof(json));

        var result = new List<CrmTradeDocumentParsedRow>(elements.Length);
        for (var index = 0; index < elements.Length; index++)
        {
            var element = elements[index];
            if (element.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"Trade-document OCR row {index} must be an object.", nameof(json));
            string? Get(string name)
                => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
            var errors = new List<string>();
            var documentNumber = Trim(Get("documentNumber"));
            var orderReference = Trim(Get("orderReference"));
            var documentType = (CrmTradeDocumentType)(GetInt(element, "documentType") ?? 0);
            if (!Enum.IsDefined(documentType)) errors.Add("invalid_document_type");
            if (documentNumber is null) errors.Add("document_number_required");
            if (orderReference is null) errors.Add("order_reference_required");

            var currency = Trim(Get("currency"))?.ToUpperInvariant();
            if (currency is not null && !CurrencyPattern().IsMatch(currency)) errors.Add("invalid_currency");
            var totalAmount = GetDecimal(element, "totalAmount");
            if (totalAmount < 0) errors.Add("invalid_total_amount");

            DateTime? shipmentDate = null;
            var rawDate = Trim(Get("shipmentDate"));
            if (rawDate is not null)
            {
                if (DateTime.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedDate))
                    shipmentDate = parsedDate;
                else errors.Add("invalid_shipment_date");
            }

            var lines = ParseLines(element, errors);
            if (lines.Count == 0) errors.Add("at_least_one_line_required");

            result.Add(new CrmTradeDocumentParsedRow(
                Trim(Get("externalId")) ?? $"row-{index + 1}",
                documentType,
                documentNumber ?? string.Empty,
                orderReference ?? string.Empty,
                Trim(Get("buyer")),
                Trim(Get("seller")),
                Trim(Get("carrier")),
                Trim(Get("trackingNumber")),
                shipmentDate,
                currency,
                totalAmount,
                lines,
                Trim(Get("rawOcrText")),
                errors));
        }
        return result;
    }

    public static IReadOnlyDictionary<string, long> SuggestLines(
        IReadOnlyList<CrmTradeDocumentParsedLine> extracted,
        IReadOnlyList<SysCrmSalesOrderLine> orderLines)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in extracted)
        {
            if (string.IsNullOrWhiteSpace(line.Sku)) continue;
            var match = orderLines.FirstOrDefault(orderLine =>
                string.Equals(orderLine.ProductCode?.Trim(), line.Sku.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null) result[line.Sku] = match.BasicId;
        }
        return result;
    }

    public static (bool Allowed, string Reason) CanConfirm(CrmTradeDocumentImportStatus status, string? duplicateReason)
    {
        if (status != CrmTradeDocumentImportStatus.Pending) return (false, "not_pending");
        if (!string.IsNullOrWhiteSpace(duplicateReason)) return (false, "duplicate_blocked");
        return (true, "ok");
    }

    public static (bool Allowed, string Reason) CanReject(CrmTradeDocumentImportStatus status, string? reason)
    {
        if (status != CrmTradeDocumentImportStatus.Pending) return (false, "not_pending");
        if (string.IsNullOrWhiteSpace(reason)) return (false, "reject_reason_required");
        return (true, "ok");
    }

    public static void ValidateMapping(
        IReadOnlyList<CrmTradeDocumentParsedLine> extracted,
        IReadOnlyList<SysCrmSalesOrderLine> orderLines,
        IReadOnlyList<(long LineId, decimal Quantity)> mapped)
    {
        if (mapped.Count != extracted.Count)
            throw new ArgumentException("Every OCR line must be mapped to an order line.", nameof(mapped));
        foreach (var item in mapped)
        {
            if (item.LineId <= 0) throw new ArgumentOutOfRangeException(nameof(mapped), "Invalid order line id.");
            if (item.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(mapped), "Mapped quantity must be positive.");
            var line = orderLines.FirstOrDefault(candidate => candidate.BasicId == item.LineId)
                ?? throw new InvalidOperationException("Mapped order line does not belong to the selected order.");
            if (item.Quantity > line.Quantity - line.ReservedQuantity - line.ShippedQuantity)
                throw new InvalidOperationException("Mapped quantity exceeds the remaining order quantity.");
        }
    }

    private static IReadOnlyList<CrmTradeDocumentParsedLine> ParseLines(JsonElement element, List<string> errors)
    {
        if (!element.TryGetProperty("lines", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            errors.Add("lines_required");
            return [];
        }
        var result = new List<CrmTradeDocumentParsedLine>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string? Get(string name)
                => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
            var sku = Trim(Get("sku"));
            var quantity = GetDecimal(item, "quantity");
            if (sku is null || quantity <= 0) errors.Add("invalid_line");
            result.Add(new CrmTradeDocumentParsedLine(sku ?? string.Empty, quantity, Trim(Get("description"))));
        }
        return result;
    }

    private static int? GetInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static decimal GetDecimal(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number)
            ? number
            : 0m;

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}