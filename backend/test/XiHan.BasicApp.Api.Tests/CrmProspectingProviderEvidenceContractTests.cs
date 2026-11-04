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
}
