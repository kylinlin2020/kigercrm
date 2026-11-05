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
    public void Create_ShouldHashTheSameBoundValuesStoredOnCandidate()
    {
        var longTitle = new string('T', 350);
        var longSummary = new string('S', 5100);
        var record = new CrmProspectingRawRecord(
            "record-oversized", "source", longTitle, "   ",
            " https://buyer.example " + new string('w', 510), new string('C', 220),
            "buyer@example.com", longSummary)
        {
            SourceUrl = "https://buyer.example/tender/bounded"
        };

        var evidence = CrmProspectingEvidenceFactory.Create(
            8, new CrmProspectingRawEnvelope("provider", record), Retrieved);

        static string Hash(string value) => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

        Assert.Contains(evidence, item =>
            item.FieldName == "company_name" && item.FieldValueHash == Hash(longTitle[..200]));
        Assert.Contains(evidence, item =>
            item.FieldName == "title" && item.FieldValueHash == Hash(longTitle[..300]));
        Assert.Contains(evidence, item =>
            item.FieldName == "summary" && item.FieldValueHash == Hash(longSummary[..5000]));
        Assert.Contains(evidence, item => item.FieldName == "website" && item.FieldValueHash?.Length == 64);
        Assert.Contains(evidence, item => item.FieldName == "contact_name" && item.FieldValueHash is null);
        Assert.Contains(evidence, item => item.FieldName == "business_email" && item.FieldValueHash is null);
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
        Assert.All(evidence, item => Assert.Equal(CrmProspectingRecordIdentity.NormalizeExternalId(record), item.ProviderRecordId));
        Assert.All(evidence, item => Assert.Equal(CrmProspectingRecordIdentity.GetPayloadHash(record), item.RawPayloadHash));
        Assert.All(evidence, item => Assert.Equal(64, item.RawPayloadHash.Length));
        Assert.All(evidence, item => Assert.Equal(64, item.SourceUrlHash.Length));
    }
    [Theory]
    [InlineData("record-123", "record-123")]
    [InlineData("  record-123  ", "record-123")]
    public void Create_ShouldUseTheSameNormalizedProviderIdentityAsCandidate(string externalId, string expected)
    {
        var record = new CrmProspectingRawRecord(externalId, "source", "title", "buyer", null, null, null, null)
        {
            SourceUrl = "https://buyer.example/tender/shared-identity"
        };

        var evidence = CrmProspectingEvidenceFactory.Create(
            12, new CrmProspectingRawEnvelope("provider", record), Retrieved);

        Assert.NotEmpty(evidence);
        Assert.Equal(expected, CrmProspectingRecordIdentity.NormalizeExternalId(record));
        Assert.All(evidence, item => Assert.Equal(expected, item.ProviderRecordId));
    }

    [Fact]
    public void Create_ShouldHashOverlongProviderIdentityConsistently()
    {
        var record = new CrmProspectingRawRecord(new string('x', 250), "source", "title", "buyer", null, null, null, null)
        {
            SourceUrl = "https://buyer.example/tender/long-id"
        };
        var expected = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(record.ExternalId.Trim())));

        var evidence = CrmProspectingEvidenceFactory.Create(
            13, new CrmProspectingRawEnvelope("provider", record), Retrieved);

        Assert.Equal(expected, CrmProspectingRecordIdentity.NormalizeExternalId(record));
        Assert.All(evidence, item => Assert.Equal(expected, item.ProviderRecordId));
    }

    [Fact]
    public void Create_ShouldFingerprintChangedPayloadWhenProviderHashIsMissing()
    {
        var first = new CrmProspectingRawRecord("same-id", "source", "first title", "buyer", null, null, null, null)
        {
            SourceUrl = "https://buyer.example/tender/content",
            RawPayloadHash = "invalid"
        };
        var changed = first with { Title = "changed title" };

        var firstEvidence = CrmProspectingEvidenceFactory.Create(
            14, new CrmProspectingRawEnvelope("provider", first), Retrieved);
        var changedEvidence = CrmProspectingEvidenceFactory.Create(
            14, new CrmProspectingRawEnvelope("provider", changed), Retrieved);

        Assert.NotEmpty(firstEvidence);
        Assert.NotEmpty(changedEvidence);
        Assert.NotEqual(firstEvidence[0].RawPayloadHash, changedEvidence[0].RawPayloadHash);
        Assert.All(firstEvidence, item => Assert.Equal(CrmProspectingRecordIdentity.GetPayloadHash(first), item.RawPayloadHash));
        Assert.All(changedEvidence, item => Assert.Equal(CrmProspectingRecordIdentity.GetPayloadHash(changed), item.RawPayloadHash));
    }

    [Fact]
    public void Create_ShouldProduceStableIdentityForEquivalentRecordSerialization()
    {
        var record = new CrmProspectingRawRecord("   ", "source", "标题", "buyer", null, null, null, "内容")
        {
            SourceUrl = "https://buyer.example/tender/unicode"
        };
        var equivalent = record with { };

        Assert.Equal(
            CrmProspectingRecordIdentity.NormalizeExternalId(record),
            CrmProspectingRecordIdentity.NormalizeExternalId(equivalent));
        Assert.Equal(
            CrmProspectingRecordIdentity.GetPayloadHash(record),
            CrmProspectingRecordIdentity.GetPayloadHash(equivalent));
    }

    [Fact]
    public void Create_ShouldTreatWhitespaceOnlyExternalIdsAsTheSameFallbackIdentity()
    {
        var empty = new CrmProspectingRawRecord(string.Empty, "source", "title", "buyer", null, null, null, "summary")
        {
            SourceUrl = "https://buyer.example/tender/blank-id"
        };
        var whitespace = empty with { ExternalId = "   " };

        Assert.Equal(
            CrmProspectingRecordIdentity.NormalizeExternalId(empty),
            CrmProspectingRecordIdentity.NormalizeExternalId(whitespace));
        Assert.Equal(
            CrmProspectingRecordIdentity.GetPayloadHash(empty),
            CrmProspectingRecordIdentity.GetPayloadHash(whitespace));
    }

    [Fact]
    public void Create_ShouldKeepFallbackIdentityAndPayloadFingerprintStableAcrossRetrievalTimes()
    {
        var first = new CrmProspectingRawRecord(string.Empty, "source", "title", "buyer", null, null, null, "summary")
        {
            SourceUrl = "https://buyer.example/tender/retry",
            RetrievedAtUtc = Retrieved,
            RawPayloadHash = "invalid"
        };
        var retried = first with { RetrievedAtUtc = Retrieved.AddHours(2) };

        Assert.Equal(
            CrmProspectingRecordIdentity.NormalizeExternalId(first),
            CrmProspectingRecordIdentity.NormalizeExternalId(retried));
        Assert.Equal(
            CrmProspectingRecordIdentity.GetPayloadHash(first),
            CrmProspectingRecordIdentity.GetPayloadHash(retried));

        var firstEvidence = CrmProspectingEvidenceFactory.Create(
            15, new CrmProspectingRawEnvelope("provider", first), Retrieved);
        var retriedEvidence = CrmProspectingEvidenceFactory.Create(
            15, new CrmProspectingRawEnvelope("provider", retried), Retrieved.AddHours(2));
        Assert.Equal(firstEvidence[0].RawPayloadHash, retriedEvidence[0].RawPayloadHash);
        Assert.Equal(firstEvidence[0].ProviderRecordId, retriedEvidence[0].ProviderRecordId);
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
    public void Create_ShouldRejectNullProviderCodeAndNullRecordSafely()
    {
        var record = new CrmProspectingRawRecord("1", "source", "title", "buyer", null, null, null, null)
        {
            SourceUrl = "https://buyer.example/tender/null-guard"
        };

        Assert.Empty(CrmProspectingEvidenceFactory.Create(
            1, new CrmProspectingRawEnvelope(null!, record), Retrieved));
        Assert.Throws<ArgumentNullException>(() => CrmProspectingEvidenceFactory.Create(
            1, new CrmProspectingRawEnvelope("provider", null!), Retrieved));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldRejectBlankProviderCode(string providerCode)
    {
        var record = new CrmProspectingRawRecord("1", "source", "title", "buyer", null, null, null, null)
        {
            SourceUrl = "https://buyer.example/tender/provider"
        };

        var evidence = CrmProspectingEvidenceFactory.Create(
            1, new CrmProspectingRawEnvelope(providerCode, record), Retrieved);

        Assert.Empty(evidence);
    }

    [Fact]
    public void Create_ShouldRejectProviderCodeAndCanonicalUrlBeyondColumnLimits()
    {
        var record = new CrmProspectingRawRecord("1", "source", "title", "buyer", null, null, null, null)
        {
            SourceUrl = "https://buyer.example/" + new string('a', 2000)
        };

        var longUrlEvidence = CrmProspectingEvidenceFactory.Create(
            1, new CrmProspectingRawEnvelope("provider", record), Retrieved);
        var longProviderEvidence = CrmProspectingEvidenceFactory.Create(
            1, new CrmProspectingRawEnvelope(new string('p', 81), record with
            {
                SourceUrl = "https://buyer.example/tender/valid"
            }), Retrieved);

        Assert.Empty(longUrlEvidence);
        Assert.Empty(longProviderEvidence);
    }

    [Fact]
    public void Create_ShouldNormalizeProviderRetrievalTimestampToUtc()
    {
        var localOffset = new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.FromHours(3));
        var record = new CrmProspectingRawRecord("1", "source", "title", "buyer", null, null, null, null)
        {
            SourceUrl = "https://buyer.example/tender/time",
            RetrievedAtUtc = localOffset
        };

        var evidence = CrmProspectingEvidenceFactory.Create(
            1, new CrmProspectingRawEnvelope("provider", record), Retrieved);

        Assert.NotEmpty(evidence);
        Assert.All(evidence, item => Assert.Equal(TimeSpan.Zero, item.RetrievedAtUtc.Offset));
        Assert.All(evidence, item => Assert.Equal(localOffset.UtcDateTime, item.RetrievedAtUtc.UtcDateTime));
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
