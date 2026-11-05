using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmRorProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_MapsResearchOrganizationRegistryEvidence()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"items":[{"id":"https://ror.org/012345678","status":"active","types":["Education","Facility"],"names":[{"value":"Example Technical University","types":["ror_display"]},{"value":"ETU","types":["acronym"]}],"locations":[{"geonames_details":{"country_name":"United Kingdom","country_code":"GB"}}],"links":[{"value":"https://example.edu"}]}],"metadata":{"number_of_results":1}}"""));
        using var provider = new CrmRorProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(Request());
        var record = Assert.Single(result.Records);

        Assert.Equal("ror", provider.ProviderCode);
        Assert.Equal("ROR:012345678", record.ExternalId);
        Assert.Equal("Example Technical University", record.CompanyName);
        Assert.Equal("research_organization_registry", record.EvidenceType);
        Assert.Equal("https://ror.org/012345678", record.SourceUrl);
        Assert.Equal("https://example.edu", record.Website);
        Assert.Contains("United Kingdom", record.Summary!, StringComparison.Ordinal);
        Assert.Contains("query=water", handler.RequestUri, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_DeduplicatesAcrossKeywordsAndMarksLargeResultSetIncomplete()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"items":[{"id":"https://ror.org/012345678","status":"active","types":["Education"],"names":[{"value":"Example Institute","types":["ror_display"]}],"locations":[],"links":[]}],"metadata":{"number_of_results":28}}"""));
        using var provider = new CrmRorProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(Request());

        Assert.Equal(3, handler.CallCount);
        Assert.Single(result.Records);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Contains("first 20", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_SkipsNonCanonicalRorUrls()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"items":[{"id":"https://evil.example/organization/1","status":"active","types":["Education"],"names":[{"value":"Spoofed Institute","types":["ror_display"]}],"locations":[],"links":[]}],"metadata":{"number_of_results":1}}"""));
        using var provider = new CrmRorProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(Request());

        Assert.Empty(result.Records);
        Assert.Contains(result.Warnings, warning => warning.Contains("invalid identifier", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_PropagatesRateLimitCooldown()
    {
        var handler = new StubHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { RequestMessage = request };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            return response;
        });
        using var provider = new CrmRorProspectingProvider(new HttpClient(handler));

        await Assert.ThrowsAsync<CrmProspectingProviderRateLimitException>(() => provider.SearchAsync(Request()));
    }

    private static CrmProspectingProviderRequest Request()
        => new(1, "find pump research partners", "water pumps", "Global", "industrial equipment companies", "{}", []);

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? "";
            return Task.FromResult(factory(request, CallCount));
        }
    }
}
