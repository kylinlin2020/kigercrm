using System.Text.Json;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

public sealed record CrmBusinessCardParsedRow(
    string ExternalId,
    string? ContactName,
    string? CompanyName,
    string? Phone,
    string? Email,
    string? JobTitle,
    string? Website,
    string? RawOcrText,
    IReadOnlyList<string> Errors)
{
    public string? NormalizedEmail => CrmBusinessCardPolicy.NormalizeEmail(Email);
    public string? PhoneDigits => CrmBusinessCardPolicy.NormalizePhoneDigits(Phone);
    public string? WebsiteHost => CrmBusinessCardPolicy.NormalizeWebsiteHost(Website);
}

/// <summary>
/// Deterministic OCR payload parsing and review gating. No OCR engine is invoked: the operator
/// supplies extracted fields, this policy only validates, normalises and duplicates them.
/// </summary>
public static partial class CrmBusinessCardPolicy
{
    public const int MaxRows = 200;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    public static IReadOnlyList<CrmBusinessCardParsedRow> ParseRows(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Business-card OCR payload must be a JSON array.", nameof(json));
        var rows = document.RootElement.EnumerateArray().ToArray();
        if (rows.Length is < 1 or > MaxRows)
            throw new ArgumentException($"Business-card OCR payload must contain 1..{MaxRows} rows.", nameof(json));
        var result = new List<CrmBusinessCardParsedRow>(rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            var element = rows[index];
            if (element.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"Business-card OCR row {index} must be an object.", nameof(json));
            string? Get(string name)
                => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
            var contactName = Trim(Get("contactName"));
            var companyName = Trim(Get("companyName"));
            var phone = Trim(Get("phone"));
            var email = Trim(Get("email"));
            var website = Trim(Get("website"));
            var errors = new List<string>();
            if (contactName is null && companyName is null) errors.Add("name_or_company_required");
            if (phone is null && email is null) errors.Add("contact_required");
            if (email is not null && !EmailPattern().IsMatch(email)) errors.Add("invalid_email");
            var digits = NormalizePhoneDigits(phone);
            if (phone is not null && (digits is null || digits.Length is < 7 or > 15)) errors.Add("invalid_phone");
            result.Add(new CrmBusinessCardParsedRow(
                Trim(Get("externalId")) ?? $"row-{index + 1}",
                contactName,
                companyName,
                phone,
                email,
                Trim(Get("jobTitle")),
                website,
                Trim(Get("rawOcrText")),
                errors));
        }
        return result;
    }

    public static (bool Allowed, string Reason) CanConfirm(CrmBusinessCardImportStatus status, string? duplicateReason)
    {
        if (status != CrmBusinessCardImportStatus.Pending) return (false, "not_pending");
        if (!string.IsNullOrWhiteSpace(duplicateReason)) return (false, "duplicate_blocked");
        return (true, "ok");
    }

    public static (bool Allowed, string Reason) CanReject(CrmBusinessCardImportStatus status, string? reason)
    {
        if (status != CrmBusinessCardImportStatus.Pending) return (false, "not_pending");
        if (string.IsNullOrWhiteSpace(reason)) return (false, "reject_reason_required");
        return (true, "ok");
    }

    public static string? NormalizeEmail(string? email)
    {
        var value = Trim(email);
        return value?.ToLowerInvariant();
    }

    public static string? NormalizePhoneDigits(string? phone)
    {
        var value = Trim(phone);
        if (value is null) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    public static string? NormalizeWebsiteHost(string? website)
    {
        var value = Trim(website);
        if (value is null) return null;
        var candidate = value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : $"https://{value}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.Trim().ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
