using System.Net;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingHttpHandlerFactoryTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("192.88.99.1")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("2001:db8::1")]
    [InlineData("3fff::1")]
    [InlineData("::ffff:192.168.1.1")]
    public void IsPublicAddress_ShouldRejectNonPublicAndSpecialUseAddresses(string value)
    {
        Assert.False(CrmProspectingHttpHandlerFactory.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("2606:4700:4700::1111")]
    public void IsPublicAddress_ShouldAcceptGlobalUnicastAddresses(string value)
    {
        Assert.True(CrmProspectingHttpHandlerFactory.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Fact]
    public void IsPublicAddress_ShouldRejectLimitedBroadcastAddress()
    {
        Assert.False(CrmProspectingHttpHandlerFactory.IsPublicAddress(IPAddress.None));
    }
}
