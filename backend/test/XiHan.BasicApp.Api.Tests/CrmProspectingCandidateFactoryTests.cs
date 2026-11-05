using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingCandidateFactoryTests
{
    [Fact]
    public void Create_ShouldBoundAndNormalizeCandidateFieldsWithoutChangingReviewDecision()
    {
        var record = new CrmProspectingRawRecord(
            "external-1",
            "  Official source  ",
            new string('T', 350),
            "Buyer Ltd",
            " https://buyer.example/path ",
            new string('C', 220),
            " buyer@example.com ",
            new string('S', 5100));

        var candidate = CrmProspectingCandidateFactory.Create(
            42,
            " provider ",
            record,
            "external-1",
            "Buyer Ltd",
            "buyer@example.com",
            "buyer.example",
            isDuplicate: false,
            duplicateLeadId: null,
            duplicateReason: null);

        Assert.Equal(42, candidate.RunId);
        Assert.Equal("provider", candidate.ProviderCode);
        Assert.Equal("external-1", candidate.ExternalId);
        Assert.Equal("Official source", candidate.Source);
        Assert.Equal(300, candidate.Title!.Length);
        Assert.Equal("Buyer Ltd", candidate.CompanyName);
        Assert.Equal(200, candidate.ContactName!.Length);
        Assert.Equal("buyer@example.com", candidate.BusinessEmail);
        Assert.Equal("buyer@example.com", candidate.NormalizedEmail);
        Assert.Equal("https://buyer.example/path", candidate.Website);
        Assert.Equal("buyer.example", candidate.WebsiteHost);
        Assert.Equal(5000, candidate.Summary!.Length);
        Assert.Equal(CrmProspectingCandidateStatus.PendingReview, candidate.Status);
        Assert.Null(candidate.DuplicateLeadId);
        Assert.Null(candidate.DuplicateReason);
    }

    [Fact]
    public void Create_ShouldPreserveCallerDuplicateClassificationAndReason()
    {
        var record = new CrmProspectingRawRecord(
            "external-2", "source", "title", "Buyer Ltd", null, null, null, null);

        var candidate = CrmProspectingCandidateFactory.Create(
            43,
            "provider",
            record,
            "external-2",
            "Buyer Ltd",
            null,
            null,
            isDuplicate: true,
            duplicateLeadId: 123,
            duplicateReason: "Email matches an existing lead");

        Assert.Equal(CrmProspectingCandidateStatus.Duplicate, candidate.Status);
        Assert.Equal(123, candidate.DuplicateLeadId);
        Assert.Equal("Email matches an existing lead", candidate.DuplicateReason);
    }

    [Theory]
    [InlineData("  buyer@example.com  ", "buyer@example.com")]
    [InlineData("not-an-email", null)]
    [InlineData(null, null)]
    public void NormalizeEmail_ShouldUseSharedCandidateNormalization(string? input, string? expected)
    {
        var record = new CrmProspectingRawRecord(
            "id", "source", "title", "Buyer Ltd", null, null, input, null);

        Assert.Equal(expected, CrmProspectingCandidateFactory.NormalizeEmail(record));
    }

    [Fact]
    public void NormalizeWebsiteHost_ShouldBoundInputBeforeDomainNormalization()
    {
        var record = new CrmProspectingRawRecord(
            "id", "source", "title", "Buyer Ltd", "https://buyer.example/path", null, null, null);

        Assert.Equal("buyer.example", CrmProspectingCandidateFactory.NormalizeWebsiteHost(record));
    }

    [Fact]
    public void Create_ShouldRejectInvalidRequiredIdentity()
    {
        var record = new CrmProspectingRawRecord(
            "id", "source", "title", "Buyer Ltd", null, null, null, null);

        Assert.Throws<ArgumentOutOfRangeException>(() => CrmProspectingCandidateFactory.Create(
            0, "provider", record, "id", "Buyer Ltd", null, null, false, null, null));
        Assert.Throws<ArgumentException>(() => CrmProspectingCandidateFactory.Create(
            1, " ", record, "id", "Buyer Ltd", null, null, false, null, null));
    }
}
