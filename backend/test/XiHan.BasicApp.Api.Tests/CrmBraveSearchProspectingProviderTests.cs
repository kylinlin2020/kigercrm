using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmBraveSearchProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_RequiresTenantSubscriptionToken()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmBraveSearchProspectingProvider(new HttpClient(handler), (string?)null);

        var result = await provider.SearchAsync(Request());

        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_UsesTokenHeaderAndMapsHttpsResultsWithCountryTargeting()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"query":{"more_results_available":false},"web":{"results":[{"title":"Industrial Pump Supplier","url":"https://example.nl/pumps?ref=search","description":"Supplier of industrial water pumps."},{"title":"Excluded","url":"https://private.example.nl/catalog","description":"Excluded domain."},{"title":"Unsafe","url":"http://insecure.example.nl","description":"HTTP is not accepted."}]}}"""));
        using var provider = new CrmBraveSearchProspectingProvider(new HttpClient(handler), "brave-test-token");

        var result = await provider.SearchAsync(Request() with { ExcludedDomains = ["private.example.nl"] });
        var record = Assert.Single(result.Records);

        Assert.Equal("brave", provider.ProviderCode);
        Assert.Equal("Brave Search", record.Source);
        Assert.Equal("Industrial Pump Supplier", record.Title);
        Assert.Equal("https://example.nl/pumps", record.SourceUrl);
        Assert.Equal("https://example.nl/", record.Website);
        Assert.Equal("web_search_result", record.EvidenceType);
        Assert.Equal("brave-test-token", handler.SubscriptionToken);
        Assert.Contains("country=NL", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("count=20", handler.RequestUri, StringComparison.Ordinal);
        Assert.DoesNotContain("brave-test-token", handler.RequestUri, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_UsesBoundedOffsetPaginationAndMarksRemainingResultsIncomplete()
    {
        var handler = new StubHandler((request, call) => Ok(request,
            $$"""{"query":{"more_results_available":true},"web":{"results":[{"title":"Supplier {{call}}","url":"https://supplier{{call}}.nl","description":"Pump supplier"}]}}"""));
        using var provider = new CrmBraveSearchProspectingProvider(new HttpClient(handler), "test-token");

        var result = await provider.SearchAsync(Request());

        Assert.Equal(2, handler.CallCount);
        Assert.Contains("offset=0", handler.FirstRequestUri, StringComparison.Ordinal);
        Assert.Contains("offset=1", handler.SecondRequestUri, StringComparison.Ordinal);
        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Records.Count);
        Assert.Contains(result.Warnings, warning => warning.Contains("more results", StringComparison.OrdinalIgnoreCase));
    }

    private static CrmProspectingProviderRequest Request()
        => new(1, "find pump distributors", "water pumps", "Netherlands", "industrial equipment distributors", "{}", []);

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
        public string FirstRequestUri { get; private set; } = "";
        public string SecondRequestUri { get; private set; } = "";
        public string SubscriptionToken { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? "";
            if (CallCount == 1) FirstRequestUri = RequestUri;
            if (CallCount == 2) SecondRequestUri = RequestUri;
            SubscriptionToken = request.Headers.TryGetValues("X-Subscription-Token", out var values) ? values.Single() : "";
            return Task.FromResult(factory(request, CallCount));
        }
    }
}
