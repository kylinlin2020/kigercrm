using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.Services;

/// <summary>
/// Deterministic, side-effect-free lead duplicate scoring. Company name alone is intentionally a weak signal.
/// This matcher does not decide whether a lead may be blocked; callers must present candidates for review.
/// </summary>
public static partial class CrmLeadDuplicateMatcher
{
    public static IReadOnlyList<CrmLeadDuplicateMatch> FindMatches(
        SysCrmLead source,
        IEnumerable<SysCrmLead> candidates,
        int minimumScore = 50)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidates);
        if (minimumScore is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(minimumScore));

        return candidates
            .Where(candidate => candidate is not null && candidate.BasicId != source.BasicId)
            .Select(candidate => Score(source, candidate))
            .Where(match => match.Score >= minimumScore && match.MatchedSignals.Count > 0)
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.MatchedSignals.Count)
            .ThenBy(match => match.Candidate.BasicId)
            .ToList();
    }

    public static CrmLeadDuplicateMatch Score(SysCrmLead source, SysCrmLead candidate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);

        var signals = new List<string>();
        var score = 0;
        if (Same(NormalizeEmail(source.Email), NormalizeEmail(candidate.Email)))
        {
            signals.Add("Email");
            score = 100;
        }

        if (Same(NormalizePhone(source.Phone), NormalizePhone(candidate.Phone)))
        {
            signals.Add("Phone");
            score = Math.Max(score, 95);
        }

        if (Same(NormalizeDomain(source.Website), NormalizeDomain(candidate.Website)))
        {
            signals.Add("WebsiteDomain");
            score = Math.Max(score, 90);
        }

        if (Same(NormalizeCompany(source.CompanyName), NormalizeCompany(candidate.CompanyName)))
        {
            signals.Add("CompanyName");
            score = Math.Max(score, 60);
        }

        var confidence = score >= 90
            ? CrmLeadDuplicateConfidence.High
            : score >= 60
                ? CrmLeadDuplicateConfidence.Medium
                : CrmLeadDuplicateConfidence.Low;

        return new CrmLeadDuplicateMatch(candidate, score, confidence, signals);
    }

    public static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var separator = normalized.IndexOf('@');
        if (separator <= 0 || separator != normalized.LastIndexOf('@') || separator == normalized.Length - 1)
            return null;

        var local = normalized[..separator];
        var domain = normalized[(separator + 1)..];
        if (local.Any(char.IsWhiteSpace) || domain.Any(char.IsWhiteSpace)
            || domain.StartsWith('.') || domain.EndsWith('.') || domain.Contains("..", StringComparison.Ordinal))
            return null;

        return local + "@" + domain;
    }

    /// <summary>Removes visual phone formatting but deliberately preserves all digits, including country code.</summary>
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || PhoneExtension().IsMatch(value)) return null;
        var digits = NonDigits().Replace(value, string.Empty);
        // A short fragment or an overlong value is not a reliable identity signal.
        // Extension-bearing values are rejected above rather than concatenating the extension.
        return digits.Length is >= 7 and <= 15 ? digits : null;
    }

    public static string? NormalizeDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var input = value.Trim();
        if (!input.Contains("://", StringComparison.Ordinal)) input = "https://" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        if (host.Length == 0 || !host.Contains('.', StringComparison.Ordinal)
            || IPAddress.TryParse(host, out _)
            || Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            return null;
        }

        return host;
    }

    public static string? NormalizeCompany(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        normalized = NonCompanyCharacters().Replace(normalized, " ");
        normalized = Whitespace().Replace(normalized, " ").Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool Same(string? left, string? right) =>
        left is not null && right is not null && string.Equals(left, right, StringComparison.Ordinal);

    [GeneratedRegex(@"[^0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonDigits();

    [GeneratedRegex(@"(?:\bext(?:ension)?\.?\s*|[;,#]\s*|\s+x\s*)\d+\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhoneExtension();

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonCompanyCharacters();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}

public enum CrmLeadDuplicateConfidence
{
    Low = 0,
    Medium = 1,
    High = 2
}

public sealed record CrmLeadDuplicateMatch(
    SysCrmLead Candidate,
    int Score,
    CrmLeadDuplicateConfidence Confidence,
    IReadOnlyList<string> MatchedSignals);
