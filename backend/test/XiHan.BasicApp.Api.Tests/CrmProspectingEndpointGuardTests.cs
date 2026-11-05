using XiHan.BasicApp.Crm.Infrastructure.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingEndpointGuardTests
{
    private static readonly Uri ApprovedEndpoint =
        new("https://api.example.gov/v1/notices");

    [Theory]
    [InlineData("https://api.example.gov/v1/notices")]
    [InlineData("https://API.EXAMPLE.GOV/v1/notices?page=2")]
    [InlineData("https://api.example.gov:443/v1/notices?cursor=abc")]
    public void IsApprovedResponseUri_ShouldAcceptSameHttpsEndpoint(string actual)
    {
        Assert.True(CrmProspectingEndpointGuard.IsApprovedResponseUri(new Uri(actual), ApprovedEndpoint));
    }

    [Theory]
    [InlineData("http://api.example.gov/v1/notices")]
    [InlineData("https://api.example.gov:8443/v1/notices")]
    [InlineData("https://user:password@api.example.gov/v1/notices")]
    [InlineData("https://api.example.gov.evil.test/v1/notices")]
    [InlineData("https://evil.test/v1/notices")]
    [InlineData("https://api.example.gov/v1/other")]
    [InlineData("https://api.example.gov/v1/notices#fragment")]
    public void IsApprovedResponseUri_ShouldRejectDifferentEndpoint(string actual)
    {
        Assert.False(CrmProspectingEndpointGuard.IsApprovedResponseUri(new Uri(actual), ApprovedEndpoint));
    }

    [Fact]
    public void IsApprovedResponseUri_ShouldRejectNullActualUri()
    {
        Assert.False(CrmProspectingEndpointGuard.IsApprovedResponseUri(null, ApprovedEndpoint));
    }

    [Fact]
    public void IsApprovedResponseUri_ShouldRejectAnUntrustedApprovedEndpoint()
    {
        var invalidApprovedEndpoint = new Uri("http://api.example.gov/v1/notices");

        Assert.False(CrmProspectingEndpointGuard.IsApprovedResponseUri(
            new Uri("https://api.example.gov/v1/notices"),
            invalidApprovedEndpoint));
    }
}
