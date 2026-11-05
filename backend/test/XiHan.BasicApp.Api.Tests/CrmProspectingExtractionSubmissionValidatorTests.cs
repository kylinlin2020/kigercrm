using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingExtractionSubmissionValidatorTests
{
    private static CrmProspectingExtractionSubmission Submission(
        string providerCode = "eurostat_comext",
        string extractionId = "job-123")
        => new(
            new CrmProspectingExtractionReference(providerCode, extractionId),
            new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero),
            TimeSpan.FromMinutes(1),
            "{\"accepted\":true}");

    [Fact]
    public void TryValidate_AcceptsMatchingBoundedAcknowledgement()
    {
        Assert.True(CrmProspectingExtractionSubmissionValidator.TryValidate(
            Submission(), "EUROSTAT_COMEXT", out var failure));
        Assert.Equal(string.Empty, failure);
    }

    [Fact]
    public void TryValidate_RejectsProviderIdentityMismatch()
    {
        Assert.False(CrmProspectingExtractionSubmissionValidator.TryValidate(
            Submission(providerCode: "other_provider"), "eurostat_comext", out var failure));
        Assert.Equal("provider_identity_mismatch", failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("job\r\nforged")]
    public void TryValidate_RejectsInvalidOpaqueExtractionIds(string extractionId)
    {
        Assert.False(CrmProspectingExtractionSubmissionValidator.TryValidate(
            Submission(extractionId: extractionId), "eurostat_comext", out var failure));
        Assert.Equal("invalid_provider_extraction_id", failure);
    }

    [Fact]
    public void TryValidate_RejectsOversizedExtractionId()
    {
        Assert.False(CrmProspectingExtractionSubmissionValidator.TryValidate(
            Submission(extractionId: new string('x',
                CrmProspectingExtractionSubmissionValidator.MaxProviderExtractionIdLength + 1)),
            "eurostat_comext", out var failure));
        Assert.Equal("invalid_provider_extraction_id", failure);
    }

    [Fact]
    public void TryValidate_RejectsExpiryNotAfterSubmission()
    {
        var submission = Submission() with
        {
            ExpiresAtUtc = new DateTimeOffset(2026, 11, 5, 11, 59, 59, TimeSpan.Zero)
        };

        Assert.False(CrmProspectingExtractionSubmissionValidator.TryValidate(
            submission, "eurostat_comext", out var failure));
        Assert.Equal("invalid_provider_expiry", failure);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(86401)]
    public void TryValidate_RejectsOutOfRangePollHints(int seconds)
    {
        var submission = Submission() with { PollAfter = TimeSpan.FromSeconds(seconds) };

        Assert.False(CrmProspectingExtractionSubmissionValidator.TryValidate(
            submission, "eurostat_comext", out var failure));
        Assert.Equal("invalid_poll_after", failure);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not-json")]
    public void TryValidate_RejectsNonObjectOrMalformedSafeMetadata(string metadata)
    {
        var submission = Submission() with { SafeMetadataJson = metadata };

        Assert.False(CrmProspectingExtractionSubmissionValidator.TryValidate(
            submission, "eurostat_comext", out var failure));
        Assert.Equal("invalid_safe_metadata", failure);
    }

    [Fact]
    public void TryValidate_RejectsOversizedSafeMetadata()
    {
        var submission = Submission() with
        {
            SafeMetadataJson = "{\"value\":\"" + new string('x',
                CrmProspectingExtractionSubmissionValidator.MaxSafeMetadataJsonLength) + "\"}"
        };

        Assert.False(CrmProspectingExtractionSubmissionValidator.TryValidate(
            submission, "eurostat_comext", out var failure));
        Assert.Equal("safe_metadata_limit_exceeded", failure);
    }
}
