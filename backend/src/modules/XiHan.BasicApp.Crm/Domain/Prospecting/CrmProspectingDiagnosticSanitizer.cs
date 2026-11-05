using System.Text.RegularExpressions;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Removes common secrets and URL query/fragment values from persisted diagnostics.</summary>
public static partial class CrmProspectingDiagnosticSanitizer
{
    public static string Sanitize(string? value, int maxLength)
    {
        if (maxLength < 0) throw new ArgumentOutOfRangeException(nameof(maxLength));
        if (string.IsNullOrWhiteSpace(value) || maxLength == 0) return string.Empty;

        var sanitized = string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character));
        sanitized = DenuePathSecret().Replace(sanitized, "$1[REDACTED]");
        sanitized = UrlSensitiveSuffix().Replace(sanitized, "$1");
        sanitized = AuthorizationSecret().Replace(sanitized, "$1 [REDACTED]");
        sanitized = NamedSecret().Replace(sanitized, "$1=[REDACTED]");
        sanitized = sanitized.Trim();
        return sanitized.Length > maxLength ? sanitized[..maxLength] : sanitized;
    }

    [GeneratedRegex(@"(?i)(https?://www\.inegi\.org\.mx/app/api/denue/v1/consulta/Buscar/[^/\s?#]+/[^/\s?#]+/[^/\s?#]+/[^/\s?#]+/)[^/\s?#]+")]
    private static partial Regex DenuePathSecret();

    [GeneratedRegex(@"(?i)(https?://[^\s?#]+)[^\s]*")]
    private static partial Regex UrlSensitiveSuffix();

    [GeneratedRegex(@"(?i)\b(Bearer|Basic)\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex AuthorizationSecret();

    [GeneratedRegex(@"(?i)\b(ocp[-_]?apim[-_]?subscription[-_]?key|subscription[_-]?key|api[_-]?key|access[_-]?token|refresh[_-]?token|token|secret|password|client_secret|key)\s*[:=]\s*[""']?[^\s&,""']+")]
    private static partial Regex NamedSecret();
}
