using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingCoverageMemoryPolicyTests
{
    private static CrmProspectingCoverageScope Scope(string provider = "gleif")
        => new(provider,
            ["industrial pump", "generator"],
            ["Germany", "France"],
            ["Distributor", "Importer"],
            ["water treatment"],
            ["consumer"],
            ["example.com"],
            "planner-v1");

    [Fact]
    public void CreateScopeFingerprint_ShouldIgnoreOrderingAndWhitespace()
    {
        var first = CrmProspectingCoverageMemoryPolicy.CreateScopeFingerprint(Scope());
        var reordered = CrmProspectingCoverageMemoryPolicy.CreateScopeFingerprint(Scope() with
        {
            Products = [" generator ", "industrial   pump"],
            Markets = ["France", "Germany"],
            CustomerTypes = ["Importer", "Distributor"]
        });

        Assert.Equal(first, reordered);
    }

    [Fact]
    public void CreateScopeFingerprint_ShouldChangeWhenStableSearchScopeChanges()
    {
        var first = CrmProspectingCoverageMemoryPolicy.CreateScopeFingerprint(Scope());
        var changed = CrmProspectingCoverageMemoryPolicy.CreateScopeFingerprint(Scope() with
        {
            ExcludedDomains = ["example.com", "blocked.example"]
        });

        Assert.NotEqual(first, changed);
        Assert.NotEqual(first,
            CrmProspectingCoverageMemoryPolicy.CreateScopeFingerprint(Scope("serper")));
    }

    [Fact]
    public void CreateObservationKey_ShouldBeStableAcrossRetrievalAttempts()
    {
        var record = new CrmProspectingRawRecord(
            "record-001", "official_registry", "Company record", "Example Pumps",
            "https://example.com", null, null, "Registered company")
        {
            RetrievedAtUtc = DateTimeOffset.Parse("2026-11-01T10:00:00Z")
        };
        var retry = record with
        {
            RetrievedAtUtc = DateTimeOffset.Parse("2026-11-06T10:00:00Z")
        };

        Assert.Equal(
            CrmProspectingCoverageMemoryPolicy.CreateObservationKey("GLEIF", record),
            CrmProspectingCoverageMemoryPolicy.CreateObservationKey("gleif", retry));
    }

    [Theory]
    [InlineData(CrmProspectingExtractionStatus.Completed, true, 0, false, false, false, true)]
    [InlineData(CrmProspectingExtractionStatus.Completed, false, 0, false, false, false, false)]
    [InlineData(CrmProspectingExtractionStatus.Completed, true, 1, false, false, false, false)]
    [InlineData(CrmProspectingExtractionStatus.Completed, true, 0, true, false, false, false)]
    [InlineData(CrmProspectingExtractionStatus.Completed, true, 0, false, true, false, false)]
    [InlineData(CrmProspectingExtractionStatus.Completed, true, 0, false, false, true, false)]
    [InlineData(CrmProspectingExtractionStatus.Failed, true, 0, false, false, false, false)]
    [InlineData(CrmProspectingExtractionStatus.NeedsReview, true, 0, false, false, false, false)]
    public void CanAdvanceCompleteWatermark_ShouldRequireEntireSuccessfulScan(
        CrmProspectingExtractionStatus status,
        bool everyPageComplete,
        int failureCount,
        bool limitReached,
        bool cancelled,
        bool needsReview,
        bool expected)
        => Assert.Equal(expected, CrmProspectingCoverageMemoryPolicy.CanAdvanceCompleteWatermark(
            status, everyPageComplete, failureCount, limitReached, cancelled, needsReview));

    [Fact]
    public void CreateScopeFingerprint_ShouldRejectControlCharacters()
        => Assert.Throws<ArgumentException>(() =>
            CrmProspectingCoverageMemoryPolicy.CreateScopeFingerprint(Scope() with
            {
                Products = ["industrial\npump"]
            }));
}
