using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmSecEdgarProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_RequiresUSMarketAndFairAccessContact()
    {
        var handler = new StubHandler((request, _) => Ok(request, """{"0":{"cik_str":1234,"ticker":"PUMP","title":"Pump Systems Inc."}}"""));
        using var provider = new CrmSecEdgarProspectingProvider(new HttpClient(handler), "KigerCRM admin@example.com");

        var skipped = await provider.SearchAsync(Request() with { TargetMarkets = "Germany" });
        Assert.Empty(skipped.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.True(skipped.IsComplete);

        using var missingContact = new CrmSecEdgarProspectingProvider(new HttpClient(new StubHandler((request, _) => Ok(request, "{}"))), "KigerCRM");
        var invalid = await missingContact.SearchAsync(Request());
        Assert.Empty(invalid.Records);
        Assert.False(invalid.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_UsesFairAccessUserAgentAndMapsUSCompanyIdentity()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"0":{"cik_str":1234,"ticker":"PUMP","title":"Pump Systems Inc."},"1":{"cik_str":5678,"ticker":"OTHER","title":"Other Holdings Inc."}}"""));
        using var provider = new CrmSecEdgarProspectingProvider(new HttpClient(handler), "KigerCRM admin@example.com");

        var result = await provider.SearchAsync(Request());

        var record = Assert.Single(result.Records);
        Assert.Equal("sec_edgar", provider.ProviderCode);
        Assert.Equal("Pump Systems Inc.", record.CompanyName);
        Assert.Equal("SEC:CIK:0000001234", record.ExternalId);
        Assert.Equal("https://www.sec.gov/edgar/browse/?CIK=0000001234&owner=exclude", record.SourceUrl);
        Assert.Equal("us_sec_company_directory", record.EvidenceType);
        Assert.Equal("KigerCRM admin@example.com", handler.UserAgent);
        Assert.Equal("GET", handler.Method);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_Propagates429Cooldown()
    {
        var handler = new StubHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { RequestMessage = request };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return response;
        });
        using var provider = new CrmSecEdgarProspectingProvider(new HttpClient(handler), "KigerCRM admin@example.com");

        await Assert.ThrowsAsync<CrmProspectingProviderRateLimitException>(() => provider.SearchAsync(Request()));
    }

    private static CrmProspectingProviderRequest Request()
        => new(1, "find pump companies", "water pumps", "United States",
            "industrial pump manufacturers", "{}", []);

    private static HttpResponseMessage Ok(HttpRequestMessage request, string json)
        => new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string Method { get; private set; } = "";
        public string UserAgent { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method.Method;
            UserAgent = request.Headers.TryGetValues("User-Agent", out var values) ? values.Single() : "";
            return Task.FromResult(factory(request, CallCount));
        }
    }
}
