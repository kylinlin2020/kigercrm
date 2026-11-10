using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmSerpApiProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_RequiresTenantApiKey()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmSerpApiProspectingProvider(new HttpClient(handler), (string?)null);
        var result = await provider.SearchAsync(Request());
        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_MapsHttpsOrganicResultsAndNeverCopiesCredentialIntoEvidence()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"organic_results":[{"title":"Pump Manufacturer","link":"https://example.com/about?utm=1","snippet":"Pump manufacturer"},{"title":"Unsafe","link":"http://unsafe.example.com","snippet":"Skip"}],"serpapi_pagination":{}}"""));
        using var provider = new CrmSerpApiProspectingProvider(new HttpClient(handler), "serp-secret");
        var result = await provider.SearchAsync(Request());
        var record = Assert.Single(result.Records);
        Assert.Equal("serpapi", provider.ProviderCode);
        Assert.Equal("SerpApi", record.Source);
        Assert.Equal("https://example.com/about", record.SourceUrl);
        Assert.Equal("web_search_result", record.EvidenceType);
        Assert.Contains("api_key=serp-secret", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("gl=nl", handler.RequestUri, StringComparison.Ordinal);
        Assert.DoesNotContain("serp-secret", record.SourceUrl!, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_UsesBoundedPaginationAndMarksRemainingResultsIncomplete()
    {
        var handler = new StubHandler((request, call) => Ok(request,
            $$$"""{"organic_results":[{"title":"Supplier {{{call}}}","link":"https://supplier{{{call}}}.com","snippet":"Pump supplier"}],"serpapi_pagination":{"next":"https://serpapi.com/search?start={{{call*10}}}"}}"""));
        using var provider = new CrmSerpApiProspectingProvider(new HttpClient(handler), "key");
        var result = await provider.SearchAsync(Request());
        Assert.Equal(2, handler.CallCount);
        Assert.Contains("start=0", handler.FirstRequestUri, StringComparison.Ordinal);
        Assert.Contains("start=10", handler.SecondRequestUri, StringComparison.Ordinal);
        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Records.Count);
    }

    private static CrmProspectingProviderRequest Request()
        => new(1, "find pump distributors", "water pumps", "Netherlands", "industrial equipment distributors", "{}", []);

    private static HttpResponseMessage Ok(HttpRequestMessage request, string json)
        => new(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestUri { get; private set; } = "";
        public string FirstRequestUri { get; private set; } = "";
        public string SecondRequestUri { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? "";
            if (CallCount == 1) FirstRequestUri = RequestUri;
            if (CallCount == 2) SecondRequestUri = RequestUri;
            return Task.FromResult(factory(request, CallCount));
        }
    }
}
