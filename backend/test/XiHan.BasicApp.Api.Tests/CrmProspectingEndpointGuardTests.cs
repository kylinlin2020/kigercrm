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

public sealed class CrmProspectingSafeHttpHandlerTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("2001:4860:4860::8888", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("198.19.0.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("240.0.0.1", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    public void IsPublicAddress_ShouldApplyConservativePublicUnicastPolicy(string value, bool expected)
    {
        Assert.Equal(expected,
            CrmProspectingHttpHandlerFactory.IsPublicAddress(System.Net.IPAddress.Parse(value)));
    }

    [Fact]
    public void Create_ShouldDisableRedirectsAndSystemProxyAndInstallPinnedConnector()
    {
        using var handler = CrmProspectingHttpHandlerFactory.Create("api.example.gov");

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);
    }

    [Fact]
    public void Create_ShouldRequireAnApprovedHost()
    {
        Assert.Throws<ArgumentException>(() => CrmProspectingHttpHandlerFactory.Create());
        Assert.Throws<ArgumentException>(() => CrmProspectingHttpHandlerFactory.Create(" "));
    }
}
