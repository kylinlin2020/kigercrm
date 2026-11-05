using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmSerperSearchProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_RequiresTenantApiKey()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmSerperSearchProspectingProvider(new HttpClient(handler), (string?)null);
        var result = await provider.SearchAsync(Request());
        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_UsesHeaderMapsHttpsOrganicResultsAndFiltersExcludedDomains()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"organic":[{"title":"Pump Distributor","link":"https://example.nl/pumps?ref=1","snippet":"Industrial pumps"},{"title":"Excluded","link":"https://blocked.example.nl","snippet":"Skip"},{"title":"HTTP","link":"http://insecure.example.nl","snippet":"Skip"}]}"""));
        using var provider = new CrmSerperSearchProspectingProvider(new HttpClient(handler), "serper-secret");
        var result = await provider.SearchAsync(Request() with { ExcludedDomains = ["blocked.example.nl"] });
        var record = Assert.Single(result.Records);
        Assert.Equal("serper", provider.ProviderCode);
        Assert.Equal("Serper", record.Source);
        Assert.Equal("Pump Distributor", record.Title);
        Assert.Equal("https://example.nl/pumps", record.SourceUrl);
        Assert.Equal("web_search_result", record.EvidenceType);
        Assert.Equal("serper-secret", handler.ApiKey);
        Assert.Contains("\"gl\":\"nl\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"num\":20", handler.Body, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_FullPageMarksCoverageIncomplete()
    {
        var results = string.Join(",", Enumerable.Range(1, 20).Select(i => $$"""{"title":"Supplier {{i}}","link":"https://supplier{{i}}.com","snippet":"Pump supplier"}"""));
        var handler = new StubHandler((request, _) => Ok(request, $$"""{"organic":[{{results}}]}"""));
        using var provider = new CrmSerperSearchProspectingProvider(new HttpClient(handler), "key");
        var result = await provider.SearchAsync(Request());
        Assert.Equal(20, result.Records.Count);
        Assert.False(result.IsComplete);
    }

    private static CrmProspectingProviderRequest Request()
        => new(1, "find pump distributors", "water pumps", "Netherlands", "industrial equipment distributors", "{}", []);

    private static HttpResponseMessage Ok(HttpRequestMessage request, string json)
        => new(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string ApiKey { get; private set; } = "";
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            ApiKey = request.Headers.TryGetValues("X-API-KEY", out var values) ? values.Single() : "";
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return factory(request, CallCount);
        }
    }
}
