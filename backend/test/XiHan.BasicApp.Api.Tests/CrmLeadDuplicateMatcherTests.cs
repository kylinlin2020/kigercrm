using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Services;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmLeadDuplicateMatcherTests
{
    [Fact]
    public void Score_ShouldTreatEmailAsHighConfidence()
    {
        var source = new SysCrmLead { BasicId = 1, CompanyName = "Acme", Email = " SALES@ACME.COM " };
        var candidate = new SysCrmLead { BasicId = 2, CompanyName = "Different name", Email = "sales@acme.com" };

        var result = CrmLeadDuplicateMatcher.Score(source, candidate);

        Assert.Equal(100, result.Score);
        Assert.Equal(CrmLeadDuplicateConfidence.High, result.Confidence);
        Assert.Contains("Email", result.MatchedSignals);
    }

    [Fact]
    public void Score_ShouldNormalizePhoneFormattingWithoutDroppingCountryDigits()
    {
        var source = new SysCrmLead { BasicId = 1, CompanyName = "Acme", Phone = "+1 (415) 555-0123" };
        var candidate = new SysCrmLead { BasicId = 2, CompanyName = "Other", Phone = "1-415-555-0123" };

        var result = CrmLeadDuplicateMatcher.Score(source, candidate);

        Assert.Equal(95, result.Score);
        Assert.Contains("Phone", result.MatchedSignals);
    }

    [Fact]
    public void Score_ShouldCompareWebsiteHostIgnoringSchemeWwwAndPath()
    {
        var source = new SysCrmLead { BasicId = 1, CompanyName = "Acme", Website = "https://www.acme.example/products" };
        var candidate = new SysCrmLead { BasicId = 2, CompanyName = "Other", Website = "http://acme.example/" };

        var result = CrmLeadDuplicateMatcher.Score(source, candidate);

        Assert.Equal(90, result.Score);
        Assert.Equal(CrmLeadDuplicateConfidence.High, result.Confidence);
        Assert.Contains("WebsiteDomain", result.MatchedSignals);
    }

    [Fact]
    public void Score_ShouldKeepCompanyNameAsMediumConfidenceOnly()
    {
        var source = new SysCrmLead { BasicId = 1, CompanyName = "Acme Industrial, Inc." };
        var candidate = new SysCrmLead { BasicId = 2, CompanyName = "  ACME INDUSTRIAL INC " };

        var result = CrmLeadDuplicateMatcher.Score(source, candidate);

        Assert.Equal(60, result.Score);
        Assert.Equal(CrmLeadDuplicateConfidence.Medium, result.Confidence);
        Assert.Contains("CompanyName", result.MatchedSignals);
    }

    [Fact]
    public void FindMatches_ShouldExcludeSourceAndIgnoreEmptySignals()
    {
        var source = new SysCrmLead { BasicId = 1, CompanyName = "Acme" };
        var sameRecord = new SysCrmLead { BasicId = 1, CompanyName = "Acme" };
        var unrelatedEmpty = new SysCrmLead { BasicId = 2, CompanyName = "Different", Email = null, Phone = null, Website = null };
        var companyMatch = new SysCrmLead { BasicId = 3, CompanyName = "ACME" };
        var highMatch = new SysCrmLead { BasicId = 4, CompanyName = "Other", Email = "a@b.example" };
        source.Email = "a@b.example";

        var results = CrmLeadDuplicateMatcher.FindMatches(source, [sameRecord, unrelatedEmpty, companyMatch, highMatch]);

        Assert.Equal(new long[] { 4, 3 }, results.Select(x => x.Candidate.BasicId));
        Assert.Equal(CrmLeadDuplicateConfidence.High, results[0].Confidence);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("person@@example.com")]
    [InlineData("person@example.com.")]
    [InlineData("person name@example.com")]
    public void NormalizeEmail_ShouldRejectMalformedAddresses(string value)
    {
        Assert.Null(CrmLeadDuplicateMatcher.NormalizeEmail(value));
    }

    [Fact]
    public void Score_ShouldNotTreatEqualMalformedEmailsOrWebsitesAsDuplicateSignals()
    {
        var source = new SysCrmLead
        {
            BasicId = 1,
            CompanyName = "Source Company",
            Email = "not-an-email",
            Website = "http://[invalid"
        };
        var candidate = new SysCrmLead
        {
            BasicId = 2,
            CompanyName = "Different Company",
            Email = "not-an-email",
            Website = "http://[invalid"
        };

        var result = CrmLeadDuplicateMatcher.Score(source, candidate);

        Assert.Equal(0, result.Score);
        Assert.Empty(result.MatchedSignals);
        Assert.Null(CrmLeadDuplicateMatcher.NormalizeDomain("http://[invalid"));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("123456")]
    [InlineData("1234567890123456")]
    public void NormalizePhone_ShouldRejectTooShortOrOverlongIdentityFragments(string value)
    {
        Assert.Null(CrmLeadDuplicateMatcher.NormalizePhone(value));
    }

    [Fact]
    public void Score_ShouldNotTreatEqualShortPhoneFragmentsAsHighConfidenceDuplicates()
    {
        var source = new SysCrmLead { BasicId = 1, CompanyName = "First Company", Phone = "123" };
        var candidate = new SysCrmLead { BasicId = 2, CompanyName = "Second Company", Phone = "123" };

        var result = CrmLeadDuplicateMatcher.Score(source, candidate);

        Assert.Equal(0, result.Score);
        Assert.Empty(result.MatchedSignals);
    }

    [Theory]
    [InlineData("+1 415 555 0123 ext 45")]
    [InlineData("+1 415 555 0123 extension 45")]
    [InlineData("+1 415 555 0123 x45")]
    [InlineData("+1 415 555 0123 #45")]
    public void NormalizePhone_ShouldRejectNumbersWithExtensions(string value)
    {
        Assert.Null(CrmLeadDuplicateMatcher.NormalizePhone(value));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("https://user:password@example.com")]
    [InlineData("ftp://example.com")]
    public void NormalizeDomain_ShouldRejectNonPublicOrUnsupportedHosts(string value)
    {
        Assert.Null(CrmLeadDuplicateMatcher.NormalizeDomain(value));
    }

    [Fact]
    public void NormalizeDomain_ShouldNormalizeInternationalizedDomainName()
    {
        Assert.Equal("xn--bcher-kva.example", CrmLeadDuplicateMatcher.NormalizeDomain("https://www.bücher.example/path"));
    }

    [Fact]
    public void NormalizePhone_ShouldReturnNullWhenNoDigitsExist()
    {
        Assert.Null(CrmLeadDuplicateMatcher.NormalizePhone("ext. ABC"));
    }
}
