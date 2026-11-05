using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmWikidataProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_MapsOrganizationLikeKnowledgeGraphEntities()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"search":[{"id":"Q12345","label":"Example Pump Corporation","description":"company that manufactures industrial water pumps","concepturi":"http://www.wikidata.org/entity/Q12345"},{"id":"Q54321","label":"Example Person","description":"American engineer","concepturi":"http://www.wikidata.org/entity/Q54321"}]}"""));
        using var provider = new CrmWikidataProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(Request());

        var record = Assert.Single(result.Records);
        Assert.Equal("wikidata", provider.ProviderCode);
        Assert.Equal("WIKIDATA:Q12345", record.ExternalId);
        Assert.Equal("Example Pump Corporation", record.CompanyName);
        Assert.Equal("knowledge_graph_entity", record.EvidenceType);
        Assert.Equal("https://www.wikidata.org/wiki/Q12345", record.SourceUrl);
        Assert.Contains("company", record.Summary!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("action=wbsearchentities", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("search=water", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("KigerCRM-Prospecting/1.0", handler.UserAgent, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_DeduplicatesEntitiesAcrossKeywords()
    {
        var handler = new StubHandler((request, call) => Ok(request,
            """{"search":[{"id":"Q12345","label":"Example Pump Corporation","description":"company manufacturing pumps"}]}"""));
        using var provider = new CrmWikidataProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(Request());

        Assert.Equal(3, handler.CallCount);
        Assert.Single(result.Records);
    }

    [Fact]
    public async Task SearchAsync_MarksCappedKeywordResultsIncomplete()
    {
        var entries = string.Join(",", Enumerable.Range(1, 10)
            .Select(i => $$"""{"id":"Q{{i}}","label":"Pump Company {{i}}","description":"company manufacturing pumps"}"""));
        var handler = new StubHandler((request, _) => Ok(request, $$"""{"search":[{{entries}}]}"""));
        using var provider = new CrmWikidataProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(Request());

        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Contains("10-item cap", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_PropagatesRateLimitCooldown()
    {
        var handler = new StubHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { RequestMessage = request };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return response;
        });
        using var provider = new CrmWikidataProspectingProvider(new HttpClient(handler));

        await Assert.ThrowsAsync<CrmProspectingProviderRateLimitException>(() => provider.SearchAsync(Request()));
    }

    private static CrmProspectingProviderRequest Request()
        => new(1, "find pump distributors", "water pumps", "Global", "industrial equipment companies", "{}", []);

    private static HttpResponseMessage Ok(HttpRequestMessage request, string json)
        => new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestUri { get; private set; } = "";
        public string UserAgent { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? "";
            UserAgent = string.Join(" ", request.Headers.UserAgent.Select(value => value.ToString()));
            return Task.FromResult(factory(request, CallCount));
        }
    }
}
