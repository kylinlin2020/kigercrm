using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingEvidenceFactoryTests
{
    private static readonly DateTimeOffset Retrieved = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ShouldProduceFieldLevelUnverifiedEvidenceAndHashOnlyPublicFields()
    {
        var record = new CrmProspectingRawRecord(
            "ted-123", "official", "Generator tender", "Buyer Ltd",
            "https://buyer.example", "Jane Doe", "jane@buyer.example", "10 kVA standby generator")
        {
            SourceUrl = "https://ted.example/tender/123?tracking=abc#details",
            PublishedAtUtc = Retrieved.AddDays(-3),
            RetrievedAtUtc = Retrieved,
            RawPayloadHash = new string('A', 64),
            EvidenceType = "registry"
        };

        var evidence = CrmProspectingEvidenceFactory.Create(
            42, new CrmProspectingRawEnvelope("official-ted", record), Retrieved);

        Assert.Equal(6, evidence.Count);
        Assert.All(evidence, item => Assert.Equal("unverified", item.VerificationStatus));
        Assert.All(evidence, item => Assert.Equal("registry", item.EvidenceType));
        Assert.All(evidence, item => Assert.Equal("https://ted.example/tender/123", item.SourceUrl));
        Assert.All(evidence, item => Assert.Equal(64, item.SourceUrlHash.Length));
        Assert.All(evidence, item => Assert.False(string.IsNullOrWhiteSpace(item.ProviderRecordId)));
        Assert.All(evidence, item => Assert.Equal(64, item.RawPayloadHash.Length));
        Assert.Contains(evidence, item => item.FieldName == "business_email" && item.FieldValueHash is null);
        Assert.Contains(evidence, item => item.FieldName == "contact_name" && item.FieldValueHash is null);
        Assert.Contains(evidence, item => item.FieldName == "company_name" && item.FieldValueHash?.Length == 64);
        Assert.All(evidence, item => Assert.Equal(Retrieved, item.RetrievedAtUtc));
    }

    [Fact]
    public void Create_ShouldNormalizeNullableIdentityFieldsForUniqueIndex()
    {
        var record = new CrmProspectingRawRecord(string.Empty, "source", "title", "buyer", null, null, null, null)
        {
            SourceUrl = "https://buyer.example/tender/empty",
            RawPayloadHash = "not-a-sha256"
        };

        var evidence = CrmProspectingEvidenceFactory.Create(
            1, new CrmProspectingRawEnvelope("provider", record), Retrieved);

        Assert.NotEmpty(evidence);
        Assert.All(evidence, item => Assert.Equal(string.Empty, item.ProviderRecordId));
        Assert.All(evidence, item => Assert.Equal(string.Empty, item.RawPayloadHash));
        Assert.All(evidence, item => Assert.Equal(64, item.SourceUrlHash.Length));
    }
    [Theory]
    [InlineData("http://buyer.example/tender/1")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://localhost/private")]
    [InlineData("https://127.0.0.1/private")]
    [InlineData("https://user:password@buyer.example/tender/1")]
    [InlineData("https://service.localhost/private")]
    [InlineData("https://localhost./private")]
    [InlineData("https://service.localhost./private")]
    public void Create_ShouldRejectUnsafeOrNonHttpsSource(string sourceUrl)
    {
        var record = new CrmProspectingRawRecord("1", "source", "title", "buyer", null, null, null, null)
        {
            SourceUrl = sourceUrl
        };

        var evidence = CrmProspectingEvidenceFactory.Create(
            1, new CrmProspectingRawEnvelope("provider", record), Retrieved);

        Assert.Empty(evidence);
    }

    [Fact]
    public void Create_ShouldKeepLegacyRecordsWithoutSourceUrlUsableButWithoutEvidence()
    {
        var record = new CrmProspectingRawRecord("legacy", "search", "title", "buyer", null, null, null, null);
        var evidence = CrmProspectingEvidenceFactory.Create(
            1, new CrmProspectingRawEnvelope("legacy", record), Retrieved);

        Assert.Empty(evidence);
    }

    [Fact]
    public void Create_ShouldRejectNonUtcFallbackTime()
    {
        var record = new CrmProspectingRawRecord("1", "source", "title", "buyer", null, null, null, null);
        Assert.Throws<ArgumentException>(() => CrmProspectingEvidenceFactory.Create(
            1, new CrmProspectingRawEnvelope("provider", record),
            new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(1))));
    }

    [Fact]
    public void Create_ShouldUseUppercaseSha256OfCanonicalSourceUrlForIdentity()
    {
        var record = new CrmProspectingRawRecord("record-1", "source", "title", "buyer", null, null, null, null)
        {
            SourceUrl = "https://buyer.example/tender/identity?utm_source=mail#section",
            RawPayloadHash = new string('B', 64)
        };

        var evidence = CrmProspectingEvidenceFactory.Create(
            7, new CrmProspectingRawEnvelope("provider", record), Retrieved);

        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("https://buyer.example/tender/identity")));
        Assert.NotEmpty(evidence);
        Assert.All(evidence, item => Assert.Equal(expected, item.SourceUrlHash));
        Assert.All(evidence, item => Assert.Equal("https://buyer.example/tender/identity", item.SourceUrl));
        Assert.All(evidence, item => Assert.DoesNotContain("utm_source", item.SourceUrl));
    }
}
