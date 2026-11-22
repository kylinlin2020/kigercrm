using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Deterministic, whitelist-only {{Variable}} interpolation for outreach templates. An unknown
/// variable, an empty value or an oversized result is an explicit error: no silent fallback and no
/// arbitrary property access, so a draft can never claim a fact the source record did not contain.
/// </summary>
public static partial class CrmOutreachTemplateRenderer
{
    public const int MaxTemplateLength = 2000;
    public const int MaxRenderedLength = 4000;

    public const string CompanyName = "CompanyName";
    public const string ContactName = "ContactName";
    public const string FirstName = "FirstName";
    public const string BusinessEmail = "BusinessEmail";
    public const string Website = "Website";
    public const string WebsiteHost = "WebsiteHost";
    public const string Source = "Source";
    public const string Summary = "Summary";

    [GeneratedRegex(@"\{\{([A-Za-z][A-Za-z0-9_]*)\}\}")]
    private static partial Regex VariablePattern();

    public static IReadOnlyDictionary<string, string> BuildVariables(SysCrmProspectingCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CompanyName] = candidate.CompanyName ?? string.Empty,
            [ContactName] = candidate.ContactName ?? string.Empty,
            [FirstName] = FirstNameOf(candidate.ContactName),
            [BusinessEmail] = candidate.BusinessEmail ?? string.Empty,
            [Website] = candidate.Website ?? string.Empty,
            [WebsiteHost] = candidate.WebsiteHost ?? string.Empty,
            [Source] = candidate.Source ?? string.Empty,
            [Summary] = candidate.Summary ?? string.Empty,
        };
    }

    public static string Render(string template, IReadOnlyDictionary<string, string> variables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentNullException.ThrowIfNull(variables);
        if (template.Length > MaxTemplateLength)
            throw new ArgumentException("Template exceeds the length limit.", nameof(template));
        if (template.Any(char.IsControl))
            throw new ArgumentException("Template contains control characters.", nameof(template));

        var rendered = VariablePattern().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (!variables.TryGetValue(key, out var value))
                throw new InvalidOperationException($"Unknown template variable: {key}.");
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Template variable has no value: {key}.");
            // Raw outer text is never re-scanned, so a value containing {{...}} cannot inject a variable.
            return value;
        });

        if (rendered.Length > MaxRenderedLength)
            throw new InvalidOperationException("Rendered draft exceeds the length limit.");
        return rendered;
    }

    public static string ComputeHash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string FirstNameOf(string? contactName)
        => contactName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
}
