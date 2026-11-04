using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingProviderEvidenceContractTests
{
    [Fact]
    public void RawRecord_ShouldKeepExistingConstructorAndExposeOptionalProvenance()
    {
        var retrieved = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        var record = new CrmProspectingRawRecord(
            "record-1", "official-procurement", "Generator tender", "Example Buyer",
            "https://buyer.example/tender/1", null, null, "Standby generator procurement")
        {
            SourceUrl = "https://buyer.example/tender/1",
            PublishedAtUtc = retrieved.AddDays(-2),
            RetrievedAtUtc = retrieved,
            RawPayloadHash = new string('a', 64),
            EvidenceType = "direct"
        };

        Assert.Equal("https://buyer.example/tender/1", record.SourceUrl);
        Assert.Equal(retrieved.AddDays(-2), record.PublishedAtUtc);
        Assert.Equal(retrieved, record.RetrievedAtUtc);
        Assert.Equal(64, record.RawPayloadHash!.Length);
        Assert.Equal("direct", record.EvidenceType);
    }

    [Fact]
    public void LegacyProviderRecord_ShouldRemainConstructibleWithoutProvenance()
    {
        var record = new CrmProspectingRawRecord(
            "legacy-1", "legacy-provider", "Title", "Company", null, null, null, null);

        Assert.Null(record.SourceUrl);
        Assert.Null(record.PublishedAtUtc);
        Assert.Null(record.RetrievedAtUtc);
        Assert.Null(record.RawPayloadHash);
        Assert.Null(record.EvidenceType);
    }
    [Fact]
    public void RequestFingerprint_ShouldIgnoreRunIdJsonPropertyOrderAndExcludedDomainOrder()
    {
        var first = new CrmProspectingProviderRequest(
            1001, "  generator   procurement ", "Diesel generator", "Kenya",
            "Public utility buyer", "{\"region\":\"east\",\"limit\":20}",
            new[] { "www.example.com", "https://blocked.example/" });
        var second = new CrmProspectingProviderRequest(
            2002, "generator procurement", "Diesel generator", "Kenya",
            "Public utility buyer", "{\"limit\":20,\"region\":\"east\"}",
            new[] { "blocked.example", "example.com" });

        Assert.Equal(
            CrmProspectingRequestFingerprint.Create("EU_TED", first),
            CrmProspectingRequestFingerprint.Create("eu_ted", second));
    }

    [Fact]
    public void RequestFingerprint_ShouldChangeWhenSearchScopeChanges()
    {
        var first = new CrmProspectingProviderRequest(
            1, "generator procurement", "Diesel generator", "Kenya",
            "Public utility buyer", "{}", Array.Empty<string>());
        var changed = first with { TargetMarkets = "Ghana" };

        Assert.NotEqual(
            CrmProspectingRequestFingerprint.Create("eu_ted", first),
            CrmProspectingRequestFingerprint.Create("eu_ted", changed));
    }

    [Fact]
    public void RequestFingerprint_ShouldRejectMalformedProviderOptionsJson()
    {
        var request = new CrmProspectingProviderRequest(
            1, "objective", "scope", "market", "icp", "{not-json}", Array.Empty<string>());

        Assert.Throws<System.Text.Json.JsonException>(
            () => CrmProspectingRequestFingerprint.Create("provider", request));
    }

}
