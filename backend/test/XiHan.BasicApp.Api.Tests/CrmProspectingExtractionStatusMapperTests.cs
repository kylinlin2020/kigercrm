using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingExtractionStatusMapperTests
{
    [Theory]
    [InlineData(CrmProspectingExtractionProviderStatus.Pending, CrmProspectingExtractionStatus.Pending)]
    [InlineData(CrmProspectingExtractionProviderStatus.Running, CrmProspectingExtractionStatus.Running)]
    [InlineData(CrmProspectingExtractionProviderStatus.Succeeded, CrmProspectingExtractionStatus.Succeeded)]
    [InlineData(CrmProspectingExtractionProviderStatus.Failed, CrmProspectingExtractionStatus.Failed)]
    [InlineData(CrmProspectingExtractionProviderStatus.Expired, CrmProspectingExtractionStatus.Expired)]
    [InlineData(CrmProspectingExtractionProviderStatus.Cancelled, CrmProspectingExtractionStatus.Cancelled)]
    [InlineData(CrmProspectingExtractionProviderStatus.NeedsReview, CrmProspectingExtractionStatus.NeedsReview)]
    public void Map_MapsKnownRemoteStates(
        CrmProspectingExtractionProviderStatus remote,
        CrmProspectingExtractionStatus expected)
        => Assert.Equal(expected, CrmProspectingExtractionStatusMapper.Map(remote));

    [Fact]
    public void Map_UnknownNumericRemoteStateRequiresReview()
        => Assert.Equal(
            CrmProspectingExtractionStatus.NeedsReview,
            CrmProspectingExtractionStatusMapper.Map((CrmProspectingExtractionProviderStatus)999));

    [Fact]
    public void NormalizeRetryAt_UsesBoundedDefaultAndClampsProviderHints()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(now.AddMinutes(1), CrmProspectingExtractionStatusMapper.NormalizeRetryAtUtc(null, now));
        Assert.Equal(now.AddSeconds(5), CrmProspectingExtractionStatusMapper.NormalizeRetryAtUtc(now, now));
        Assert.Equal(now.AddMinutes(30), CrmProspectingExtractionStatusMapper.NormalizeRetryAtUtc(now.AddHours(4), now));
        Assert.Equal(now.AddMinutes(2), CrmProspectingExtractionStatusMapper.NormalizeRetryAtUtc(now.AddMinutes(2), now));
    }
}
