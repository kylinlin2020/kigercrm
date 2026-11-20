using System.Text.Json;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

public sealed record CrmCustomerImportRow(
    string ExternalId,
    string Name,
    string? LegalName,
    string? Website,
    string? CountryRegion,
    string? Industry,
    string? Source,
    string? Notes,
    IReadOnlyList<string> Errors)
{
    public string NameKey => CrmCustomerImportPolicy.NormalizeName(Name);
    public string? WebsiteHost => CrmCustomerImportPolicy.NormalizeWebsiteHost(Website);
}

public static class CrmCustomerImportPolicy
{
    public const int MaxRows = 500;

    public static readonly string[] TargetFields =
        ["name", "legalName", "website", "countryRegion", "industry", "source", "notes"];

    public static void ValidateMapping(IReadOnlyDictionary<string, string> mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (mapping.Count == 0) throw new ArgumentException("Field mapping is required.", nameof(mapping));
        if (mapping.Values.Any(value => !TargetFields.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("Mapping target must be a supported customer field.", nameof(mapping));
        if (!mapping.Values.Any(value => string.Equals(value.Trim(), "name", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Mapping must include the required 'name' field.", nameof(mapping));
    }

    public static IReadOnlyList<CrmCustomerImportRow> ParseRows(
        string rowsJson,
        IReadOnlyDictionary<string, string> mapping)
    {
        ValidateMapping(mapping);
        ArgumentException.ThrowIfNullOrWhiteSpace(rowsJson);
        using var document = JsonDocument.Parse(rowsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Customer import payload must be a JSON array.", nameof(rowsJson));
        var elements = document.RootElement.EnumerateArray().ToArray();
        if (elements.Length is < 1 or > MaxRows)
            throw new ArgumentException($"Customer import payload must contain 1..{MaxRows} rows.", nameof(rowsJson));

        var lookup = mapping.ToDictionary(
            pair => pair.Key.Trim(),
            pair => pair.Value.Trim().ToLowerInvariant(),
            StringComparer.OrdinalIgnoreCase);
        var result = new List<CrmCustomerImportRow>(elements.Length);
        for (var index = 0; index < elements.Length; index++)
        {
            var element = elements[index];
            if (element.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"Customer import row {index} must be an object.", nameof(rowsJson));
            string? Read(string target)
            {
                var pair = lookup.FirstOrDefault(item => item.Value == target);
                if (pair.Key is null) return null;
                if (!element.TryGetProperty(pair.Key, out var value)) return null;
                return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            }
            var errors = new List<string>();
            var name = Trim(Read("name"));
            var legalName = Trim(Read("legalname"));
            var website = Trim(Read("website"));
            var country = Trim(Read("countryregion"))?.ToUpperInvariant();
            var industry = Trim(Read("industry"));
            var source = Trim(Read("source")) ?? "Import";
            var notes = Trim(Read("notes"));
            if (name is null || name.Length > 200) errors.Add("invalid_name");
            if (legalName is not null && legalName.Length > 250) errors.Add("invalid_legal_name");
            if (country is not null && country.Length != 2) errors.Add("invalid_country");
            if (industry is not null && industry.Length > 120) errors.Add("invalid_industry");
            if (source.Length > 80) errors.Add("invalid_source");
            if (notes is not null && notes.Length > 2000) errors.Add("invalid_notes");

            result.Add(new CrmCustomerImportRow(
                $"row-{index + 1}",
                name ?? string.Empty,
                legalName,
                website,
                country,
                industry,
                source,
                notes,
                errors));
        }
        return result;
    }

    public static string NormalizeName(string name)
        => string.IsNullOrWhiteSpace(name) ? string.Empty : string.Join(' ', name.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static string? NormalizeWebsiteHost(string? website)
    {
        var value = Trim(website);
        if (value is null) return null;
        var candidate = value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : $"https://{value}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}