using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingExtractionPageValidatorTests
{
    private static CrmProspectingRawRecord Record(string id = "record-1")
        => new(id, "public_registry", "Company", "Example Ltd", "https://example.com", null, null, "Public record")
        {
            SourceUrl = "https://registry.example.gov/record/1",
            RawPayloadHash = new string('a', 64)
        };

    [Fact]
    public void TryValidate_AcceptsBoundedNonFinalPage()
    {
        var page = new CrmProspectingExtractionPage(
            [Record()], "cursor-2", false, false, 0, []);

        Assert.True(CrmProspectingExtractionPageValidator.TryValidate(page, out var failure));
        Assert.Equal(string.Empty, failure);
        Assert.True(CrmProspectingExtractionPageValidator.CanAdvanceCursor(page, null));
    }

    [Fact]
    public void TryValidate_RejectsControlCharactersInOpaqueCursor()
    {
        var page = new CrmProspectingExtractionPage(
            [Record()], "cursor-2\nforged-log-line", false, false, 0, []);

        Assert.False(CrmProspectingExtractionPageValidator.TryValidate(page, out var failure));
        Assert.Equal("invalid_cursor", failure);
        Assert.False(CrmProspectingExtractionPageValidator.CanAdvanceCursor(page, null));
    }

    [Fact]
    public void TryValidate_RejectsFailureCountGreaterThanPageRecordCount()
    {
        var page = new CrmProspectingExtractionPage(
            [Record()], null, true, true, 2, []);

        Assert.False(CrmProspectingExtractionPageValidator.TryValidate(page, out var failure));
        Assert.Equal("page_limit_exceeded", failure);
    }

    [Fact]
    public void TryValidate_RejectsLastPageWithoutCompleteMarker()
    {
        var page = new CrmProspectingExtractionPage(
            [Record()], null, true, false, 0, []);

        Assert.False(CrmProspectingExtractionPageValidator.TryValidate(page, out var failure));
        Assert.Equal("inconsistent_completion_marker", failure);
    }

    [Fact]
    public void TryValidate_RejectsIncompletePageClaimingComplete()
    {
        var page = new CrmProspectingExtractionPage(
            [Record()], "cursor-2", false, true, 0, []);

        Assert.False(CrmProspectingExtractionPageValidator.TryValidate(page, out var failure));
        Assert.Equal("inconsistent_completion_marker", failure);
    }

    [Fact]
    public void TryValidate_RejectsDuplicateProviderIdentitiesWithinPage()
    {
        var page = new CrmProspectingExtractionPage(
            [Record("same"), Record("same")], null, true, true, 0, []);

        Assert.False(CrmProspectingExtractionPageValidator.TryValidate(page, out var failure));
        Assert.Equal("duplicate_record_identity", failure);
    }

    [Fact]
    public void TryValidate_RejectsCredentialBearingOrNonHttpSourceUrls()
    {
        var credentialUrl = Record() with { SourceUrl = "https://user:secret@example.com/record" };
        var page = new CrmProspectingExtractionPage(
            [credentialUrl], null, true, true, 0, []);

        Assert.False(CrmProspectingExtractionPageValidator.TryValidate(page, out var failure));
        Assert.Equal("invalid_source_url", failure);
    }

    [Fact]
    public void TryValidate_RejectsOversizedPageAndCursor()
    {
        var tooMany = Enumerable.Range(0, CrmProspectingExtractionPageValidator.MaxRecordsPerPage + 1)
            .Select(index => Record("record-" + index))
            .ToArray();
        var page = new CrmProspectingExtractionPage(tooMany, null, true, true, 0, []);

        Assert.False(CrmProspectingExtractionPageValidator.TryValidate(page, out var failure));
        Assert.Equal("page_limit_exceeded", failure);

        var cursorPage = new CrmProspectingExtractionPage(
            [Record()], new string('x', CrmProspectingExtractionPageValidator.MaxCursorLength + 1), false, false, 0, []);
        Assert.False(CrmProspectingExtractionPageValidator.TryValidate(cursorPage, out failure));
        Assert.Equal("cursor_limit_exceeded", failure);
    }

    [Fact]
    public void CanAdvanceCursor_RequiresAChangingCursorOrACompleteFinalPage()
    {
        var repeated = new CrmProspectingExtractionPage(
            [Record()], "cursor-1", false, false, 0, []);
        Assert.False(CrmProspectingExtractionPageValidator.CanAdvanceCursor(repeated, "cursor-1"));

        var failedFinal = new CrmProspectingExtractionPage(
            [Record()], null, true, true, 1, []);
        Assert.False(CrmProspectingExtractionPageValidator.CanAdvanceCursor(failedFinal, null));

        var completeFinal = new CrmProspectingExtractionPage(
            [Record()], null, true, true, 0, []);
        Assert.True(CrmProspectingExtractionPageValidator.CanAdvanceCursor(completeFinal, null));
    }
}
