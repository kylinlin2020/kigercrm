using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingWebsiteDiscoveryPolicyTests
{
    private static CrmProspectingWebsiteCandidateInput Input(
        string url = "https://example-pumps.com/about",
        CrmProspectingWebsiteSourceKind source = CrmProspectingWebsiteSourceKind.SearchProvider)
        => new("Example Pumps", url, "search_provider", source,
            PageTitle: "Example Pumps | Industrial Water Treatment",
            RegistryLegalName: "Example Pumps");

    [Fact]
    public void Assess_ShouldAcceptStrongSearchCandidateForReviewOnly()
    {
        var result = CrmProspectingWebsiteDiscoveryPolicy.Assess(Input());

        Assert.True(result.AcceptedForReview);
        Assert.False(result.CanSetOfficialWebsite);
        Assert.Equal("example-pumps.com", result.Host);
        Assert.Equal("search_title_and_host_match", result.MatchClass);
        Assert.Equal("candidate_requires_independent_corroboration", result.ReasonCode);
    }

    [Fact]
    public void Assess_ShouldNeverPromoteRegistryCandidateDirectlyToOfficialWebsite()
    {
        var result = CrmProspectingWebsiteDiscoveryPolicy.Assess(
            Input("https://example-pumps.com", CrmProspectingWebsiteSourceKind.OfficialRegistry));

        Assert.True(result.AcceptedForReview);
        Assert.False(result.CanSetOfficialWebsite);
        Assert.Equal("registry_name_match", result.MatchClass);
    }

    [Theory]
    [InlineData("http://example-pumps.com", "unsafe_url_shape")]
    [InlineData("https://user:pass@example-pumps.com", "unsafe_url_shape")]
    [InlineData("https://example-pumps.com/path?redirect=1", "unsafe_url_shape")]
    [InlineData("https://example-pumps.com/path#fragment", "unsafe_url_shape")]
    [InlineData("https://localhost", "non_public_or_invalid_host")]
    [InlineData("https://printer.local", "non_public_or_invalid_host")]
    [InlineData("https://127.0.0.1", "non_public_or_invalid_host")]
    [InlineData("https://linkedin.com/company/example-pumps", "non_corporate_platform")]
    [InlineData("https://directory.example.com/listing", "insufficient_company_match")]
    public void Assess_ShouldRejectUnsafeOrWeakCandidates(string url, string reason)
    {
        var result = CrmProspectingWebsiteDiscoveryPolicy.Assess(Input(url));

        Assert.False(result.AcceptedForReview);
        Assert.False(result.CanSetOfficialWebsite);
        Assert.Equal(reason, result.ReasonCode);
    }

    [Fact]
    public void Assess_ShouldRejectControlCharactersInCompanyIdentity()
    {
        var input = Input() with { CompanyName = "Example\nPumps" };

        var result = CrmProspectingWebsiteDiscoveryPolicy.Assess(input);

        Assert.False(result.AcceptedForReview);
        Assert.Equal("invalid_text", result.ReasonCode);
    }

    [Fact]
    public void Assess_ShouldRejectSearchResultWithoutMeaningfulCompanyMatch()
    {
        var input = Input() with
        {
            CandidateUrl = "https://unrelated-domain.com",
            PageTitle = "Unrelated business directory"
        };

        var result = CrmProspectingWebsiteDiscoveryPolicy.Assess(input);

        Assert.False(result.AcceptedForReview);
        Assert.Equal("insufficient_company_match", result.ReasonCode);
    }

    [Fact]
    public void Assess_ShouldAllowExplicitManualCandidateButKeepItUnverified()
    {
        var result = CrmProspectingWebsiteDiscoveryPolicy.Assess(
            Input("https://example-pumps.com", CrmProspectingWebsiteSourceKind.ManualReview));

        Assert.True(result.AcceptedForReview);
        Assert.False(result.CanSetOfficialWebsite);
        Assert.Equal("manual_candidate", result.MatchClass);
    }
}
