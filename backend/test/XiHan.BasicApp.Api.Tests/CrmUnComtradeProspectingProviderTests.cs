using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmUnComtradeProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_UsesKeylessPreviewAndMapsTradeSignal()
    {
        var handler = new StubHandler(_ => Ok("""{"data":[{"reporterCode":842,"reporterDesc":"United States","partnerDesc":"World","cmdCode":"TOTAL","cmdDesc":"All commodities","period":"2025","flowDesc":"Export","primaryValue":123456}]}"""));
        using var provider = new CrmUnComtradeProspectingProvider(new HttpClient(handler));
        var result = await provider.SearchAsync(Request("United States"));
        var record = Assert.Single(result.Records);
        Assert.Equal("un_comtrade", provider.ProviderCode);
        Assert.Equal("UNCOMTRADE:842:2025:Export:TOTAL", record.ExternalId);
        Assert.Equal("official_trade_statistics", record.EvidenceType);
        Assert.Contains("/public/v1/preview/", handler.RequestUri, StringComparison.Ordinal);
        Assert.DoesNotContain("subscription-key", handler.RequestUri, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_UsesSubscriptionEndpointWhenKeyProvided()
    {
        var handler = new StubHandler(_ => Ok("""{"data":[{"reporterDesc":"Nigeria","partnerDesc":"World","cmdCode":"TOTAL","period":"2025","flowDesc":"Export","primaryValue":10}]}"""));
        using var provider = new CrmUnComtradeProspectingProvider(new HttpClient(handler), "test-key");
        await provider.SearchAsync(Request("Nigeria"));
        Assert.Contains("/data/v1/get/", handler.RequestUri, StringComparison.Ordinal);
        Assert.Equal("test-key", handler.SubscriptionKey);
    }

    [Fact]
    public async Task SearchAsync_RequiresSupportedReporterCountry()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmUnComtradeProspectingProvider(new HttpClient(handler));
        var result = await provider.SearchAsync(Request("Unknown country"));
        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.False(result.IsComplete);
    }

    private static CrmProspectingProviderRequest Request(string country)
        => new(1, "generator exports", "generators water pumps", country, "importers and distributors", "{}", []);

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://comtradeapi.un.org/public/v1/preview/C/A/HS?reporterCode=842"),
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestUri { get; private set; } = "";
        public string? SubscriptionKey { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? "";
            SubscriptionKey = request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var values) ? values.Single() : null;
            return Task.FromResult(factory(request));
        }
    }
}
