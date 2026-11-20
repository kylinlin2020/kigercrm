using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

public sealed record CrmCustomsRow(
    string CompanyName,
    string CountryCode,
    string HsCode,
    string Industry,
    string ExportDate,
    decimal Amount,
    string? ProductDescription,
    IReadOnlyList<string> Errors)
{
    public string NormalizedCountry => CountryCode.Trim().ToUpperInvariant();
    public string NormalizedHsCode => HsCode.Trim();
    public string NormalizedIndustry => Industry.Trim();
}

public sealed record CrmCustomsFilter(string? Industry, string? HsPrefix, string? CountryCode);

/// <summary>
/// Deterministic customs-data validation, filtering and CSV rendering. No external customs data
/// source is queried: the operator supplies rows, the tool only validates and exports them.
/// </summary>
public static partial class CrmCustomsExportPolicy
{
    public const int MaxRows = 1000;

    [GeneratedRegex("^[A-Z]{2}$", RegexOptions.IgnoreCase)]
    private static partial Regex CountryPattern();

    [GeneratedRegex("^[0-9]+$")]
    private static partial Regex DigitsPattern();

    public static IReadOnlyList<CrmCustomsRow> ParseRows(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Customs data payload must be a JSON array.", nameof(json));
        var elements = document.RootElement.EnumerateArray().ToArray();
        if (elements.Length is < 1 or > MaxRows)
            throw new ArgumentException($"Customs data payload must contain 1..{MaxRows} rows.", nameof(json));

        var result = new List<CrmCustomsRow>(elements.Length);
        for (var index = 0; index < elements.Length; index++)
        {
            var element = elements[index];
            if (element.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"Customs row {index} must be an object.", nameof(json));
            string? Get(string name)
                => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
            var errors = new List<string>();
            var company = Trim(Get("companyName"));
            var country = Trim(Get("countryCode"))?.ToUpperInvariant();
            var hs = Trim(Get("hsCode"));
            var industry = Trim(Get("industry"));
            var date = Trim(Get("exportDate"));
            if (company is null || company.Length > 200) errors.Add("invalid_company_name");
            if (country is null || !CountryPattern().IsMatch(country)) errors.Add("invalid_country_code");
            if (hs is null || !DigitsPattern().IsMatch(hs) || hs.Length is not (6 or 8 or 10)) errors.Add("invalid_hs_code");
            if (industry is null || industry.Length > 120) errors.Add("invalid_industry");
            if (date is null || !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)) errors.Add("invalid_export_date");
            var amount = GetDecimal(element, "amount");
            if (amount < 0) errors.Add("invalid_amount");
            var description = Trim(Get("productDescription"));
            if (description is not null && description.Length > 500) errors.Add("invalid_product_description");

            result.Add(new CrmCustomsRow(
                company ?? string.Empty,
                country ?? string.Empty,
                hs ?? string.Empty,
                industry ?? string.Empty,
                date ?? string.Empty,
                amount,
                description,
                errors));
        }
        return result;
    }

    public static IReadOnlyList<CrmCustomsRow> Filter(
        IReadOnlyList<CrmCustomsRow> rows,
        CrmCustomsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Where(row => row.Errors.Count == 0)
            .Where(row => string.IsNullOrWhiteSpace(filter.Industry)
                || row.NormalizedIndustry.Contains(filter.Industry.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(row => string.IsNullOrWhiteSpace(filter.HsPrefix)
                || row.NormalizedHsCode.StartsWith(filter.HsPrefix.Trim(), StringComparison.Ordinal))
            .Where(row => string.IsNullOrWhiteSpace(filter.CountryCode)
                || row.NormalizedCountry == filter.CountryCode.Trim().ToUpperInvariant())
            .OrderBy(row => row.CompanyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.NormalizedHsCode, StringComparer.Ordinal)
            .ToArray();
    }

    public static string ToCsv(IReadOnlyList<CrmCustomsRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var builder = new StringBuilder();
        builder.Append("CompanyName,CountryCode,HsCode,Industry,ExportDate,Amount,ProductDescription").Append('\n');
        foreach (var row in rows)
        {
            builder.AppendJoin(',',
                Escape(row.CompanyName),
                Escape(row.NormalizedCountry),
                Escape(row.NormalizedHsCode),
                Escape(row.NormalizedIndustry),
                Escape(row.ExportDate),
                Escape(row.Amount.ToString(CultureInfo.InvariantCulture)),
                Escape(row.ProductDescription ?? string.Empty));
            builder.Append('\n');
        }
        return builder.ToString();
    }

    private static string Escape(string value)
    {
        // Spreadsheet formula injection guard: never let exported cells start with = + - @.
        var guarded = value.Length > 0 && "=+-@".Contains(value[0]) ? "'" + value : value;
        return "\"" + guarded.Replace("\"", "\"\"") + "\"";
    }

    private static decimal GetDecimal(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number)
            ? number
            : 0m;

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}