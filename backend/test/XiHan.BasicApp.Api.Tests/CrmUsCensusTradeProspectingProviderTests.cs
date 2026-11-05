using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmUsCensusTradeProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_RequiresKeyBeforeMakingRequest()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmUsCensusTradeProspectingProvider(new HttpClient(handler), (string?)null);
        var result = await provider.SearchAsync(Request());
        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_MapsOfficialCommodityTradeStatistics()
    {
        var handler = new StubHandler(_ => Ok("""[["CTY_NAME","CTY_CODE","ALL_VAL_MO","GEN_VAL_MO","CON_VAL_MO","E_COMMODITY","E_COMMODITY_LDESC"],["Nigeria","NI","12345","12000","345","8418","Generators and generating sets"]]"""));
        using var provider = new CrmUsCensusTradeProspectingProvider(new HttpClient(handler), "test-census-key");
        var result = await provider.SearchAsync(Request());
        var record = Assert.Single(result.Records);
        Assert.Equal("us_census_trade", provider.ProviderCode);
        Assert.Equal("official_trade_statistics", record.EvidenceType);
        Assert.Contains("Generators", record.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("time=", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("key=test-census-key", handler.RequestUri, StringComparison.Ordinal);
        Assert.DoesNotContain("test-census-key", record.SourceUrl, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    private static CrmProspectingProviderRequest Request()
        => new(1, "generator exports", "generators", "Nigeria", "importers", "{}", []);

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.census.gov/data/timeseries/intltrade/exports/hs?time=2026-09"),
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestUri { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? "";
            return Task.FromResult(factory(request));
        }
    }
}
