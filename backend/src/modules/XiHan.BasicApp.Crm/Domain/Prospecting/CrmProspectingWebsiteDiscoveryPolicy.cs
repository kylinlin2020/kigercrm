using System.Net;
using System.Text;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

public enum CrmProspectingWebsiteSourceKind
{
    OfficialRegistry = 0,
    SearchProvider = 1,
    MapProvider = 2,
    ManualReview = 3
}

public sealed record CrmProspectingWebsiteCandidateInput(
    string CompanyName,
    string CandidateUrl,
    string SourceProviderCode,
    CrmProspectingWebsiteSourceKind SourceKind,
    string? PageTitle = null,
    string? RegistryLegalName = null);

public sealed record CrmProspectingWebsiteCandidateAssessment(
    bool AcceptedForReview,
    bool CanSetOfficialWebsite,
    string NormalizedUrl,
    string Host,
    string MatchClass,
    string ReasonCode);

/// <summary>
/// Validates public website-discovery candidates before any fetch or persistence.
/// Every accepted result remains unverified and review-only; this policy never declares a domain official.
/// Network access must additionally use the DNS/IP-pinned HTTP handler and fixed redirect policy.
/// </summary>
public static class CrmProspectingWebsiteDiscoveryPolicy
{
    private static readonly string[] NonCorporateHosts =
    [
        "facebook.com", "linkedin.com", "instagram.com", "youtube.com", "tiktok.com",
        "x.com", "twitter.com", "wikipedia.org", "crunchbase.com", "zoominfo.com",
        "yellowpages.com", "yelp.com", "google.com", "bing.com"
    ];

    public static CrmProspectingWebsiteCandidateAssessment Assess(CrmProspectingWebsiteCandidateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CompanyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CandidateUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SourceProviderCode);

        if (input.CompanyName.Length > 200 || input.SourceProviderCode.Length > 80 ||
            input.CompanyName.Any(char.IsControl) || input.SourceProviderCode.Any(char.IsControl))
            return Reject("invalid_text");

        if (!Uri.TryCreate(input.CandidateUrl.Trim(), UriKind.Absolute, out var uri))
            return Reject("invalid_url");

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.Query))
            return Reject("unsafe_url_shape");

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.Length is 0 or > 253 || host.Contains("..", StringComparison.Ordinal) ||
            uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 ||
            IPAddress.TryParse(host, out _) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".test", StringComparison.OrdinalIgnoreCase))
            return Reject("non_public_or_invalid_host");

        if (NonCorporateHosts.Any(blocked =>
            host.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase)))
            return Reject("non_corporate_platform");

        var company = NormalizeName(input.CompanyName);
        var pageTitle = NormalizeName(input.PageTitle ?? string.Empty);
        var registryName = NormalizeName(input.RegistryLegalName ?? string.Empty);
        var hostLabel = host.Split('.')[0].Replace('-', ' ').Replace('_', ' ');
        var hostMatch = ContainsMeaningfulCompanyToken(hostLabel, company);
        var titleMatch = ContainsMeaningfulCompanyToken(pageTitle, company);
        var registryMatch = registryName.Length > 0 &&
            string.Equals(registryName, company, StringComparison.Ordinal);

        var matchClass = input.SourceKind switch
        {
            CrmProspectingWebsiteSourceKind.OfficialRegistry when registryMatch => "registry_name_match",
            CrmProspectingWebsiteSourceKind.OfficialRegistry => "registry_source_unconfirmed",
            CrmProspectingWebsiteSourceKind.SearchProvider when titleMatch && hostMatch => "search_title_and_host_match",
            CrmProspectingWebsiteSourceKind.SearchProvider when titleMatch => "search_title_match",
            CrmProspectingWebsiteSourceKind.MapProvider when titleMatch || hostMatch => "map_name_match",
            CrmProspectingWebsiteSourceKind.ManualReview => "manual_candidate",
            _ => "weak_or_unmatched"
        };

        if (matchClass == "weak_or_unmatched")
            return Reject("insufficient_company_match");

        return new CrmProspectingWebsiteCandidateAssessment(
            AcceptedForReview: true,
            CanSetOfficialWebsite: false,
            NormalizedUrl: new UriBuilder(uri) { Host = host, Fragment = string.Empty }.Uri
                .GetLeftPart(UriPartial.Path).TrimEnd('/'),
            Host: host,
            MatchClass: matchClass,
            ReasonCode: "candidate_requires_independent_corroboration");

        CrmProspectingWebsiteCandidateAssessment Reject(string reason)
            => new(false, false, string.Empty, string.Empty, "rejected", reason);
    }

    private static string NormalizeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
                builder.Append(character);
        return string.Join(" ", builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }

    private static bool ContainsMeaningfulCompanyToken(string source, string normalizedCompany)
    {
        if (source.Length == 0 || normalizedCompany.Length == 0) return false;
        var tokens = normalizedCompany.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 4)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return tokens.Length > 0 && tokens.Any(source.Contains);
    }
}
