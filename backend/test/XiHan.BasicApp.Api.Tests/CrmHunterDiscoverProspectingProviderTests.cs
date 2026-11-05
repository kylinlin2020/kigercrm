using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmHunterDiscoverProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_RequiresTenantKeyWithoutCallingApi()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Unexpected API call."));
        using var provider = new CrmHunterDiscoverProspectingProvider(new HttpClient(handler), (string?)null);

        var result = await provider.SearchAsync(Request());

        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_PostsIcpQueryMapsCompanyDomainsAndFiltersExcludedDomains()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"data":[{"domain":"pump.example","organization":"Pump Systems","emails_count":{"personal":12,"generic":4,"total":16}},{"domain":"blocked.example","organization":"Blocked Company","emails_count":{"personal":1,"generic":1,"total":2}}],"meta":{"results":2}}"""));
        using var provider = new CrmHunterDiscoverProspectingProvider(new HttpClient(handler), "hunter-secret");

        var result = await provider.SearchAsync(Request() with { ExcludedDomains = ["blocked.example"] });

        var record = Assert.Single(result.Records);
        Assert.Equal("hunter", provider.ProviderCode);
        Assert.Equal("Pump Systems", record.CompanyName);
        Assert.Equal("https://pump.example/", record.Website);
        Assert.Equal("https://pump.example/", record.SourceUrl);
        Assert.Equal("commercial_company_discovery", record.EvidenceType);
        Assert.Contains("no email addresses were revealed", record.Summary!, StringComparison.Ordinal);
        Assert.Equal("POST", handler.Method);
        Assert.DoesNotContain("hunter-secret", handler.RequestUri, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", handler.RequestUri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("hunter-secret", handler.ApiKeyHeader);
        Assert.Contains("\"query\":", handler.Body, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_FullPageMarksCoverageIncompleteWithoutPremiumPagination()
    {
        var companies = string.Join(",", Enumerable.Range(1, 100)
            .Select(i => $$"""{"domain":"company{{i}}.example","organization":"Company {{i}}","emails_count":{"personal":0,"generic":0,"total":0}}"""));
        var handler = new StubHandler((request, _) => Ok(request, $$"""{"data":[{{companies}}],"meta":{"results":250}}"""));
        using var provider = new CrmHunterDiscoverProspectingProvider(new HttpClient(handler), "key");

        var result = await provider.SearchAsync(Request());

        Assert.Equal(100, result.Records.Count);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Contains("one page only", StringComparison.Ordinal));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_Propagates503Cooldown()
    {
        var handler = new StubHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(3));
            return response;
        });
        using var provider = new CrmHunterDiscoverProspectingProvider(new HttpClient(handler), "key");

        var exception = await Assert.ThrowsAsync<CrmProspectingProviderRateLimitException>(() => provider.SearchAsync(Request()));
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, exception.HttpStatus);
        Assert.NotNull(exception.RetryAfterAt);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_Treats403AsRequestRateLimitAndHonorsRetryAfter()
    {
        var retryAfter = DateTimeOffset.UtcNow.AddMinutes(4);
        var handler = new StubHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                RequestMessage = request,
                Content = new StringContent("""{"errors":[{"details":"rate limit"}]}""", Encoding.UTF8, "application/json")
            };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter);
            return response;
        });
        using var provider = new CrmHunterDiscoverProspectingProvider(new HttpClient(handler), "key");

        var exception = await Assert.ThrowsAsync<CrmProspectingProviderRateLimitException>(() => provider.SearchAsync(Request()));
        Assert.Equal((int)HttpStatusCode.Forbidden, exception.HttpStatus);
        Assert.NotNull(exception.RetryAfterAt);
        Assert.InRange(exception.RetryAfterAt!.Value, retryAfter.AddSeconds(-2), retryAfter.AddSeconds(2));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_Treats429AsUsageQuotaExhaustionWithoutRetryCooldown()
    {
        var handler = new StubHandler((request, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            RequestMessage = request,
            Content = new StringContent("""{"errors":[{"details":"usage limit reached"}]}""", Encoding.UTF8, "application/json")
        });
        using var provider = new CrmHunterDiscoverProspectingProvider(new HttpClient(handler), "key");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchAsync(Request()));
        Assert.Contains("usage quota is exhausted", exception.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
    }

    private static CrmProspectingProviderRequest Request()
        => new(1, "find water pump distributors", "water pumps", "United States",
            "industrial pump distributors and importers", "{}", []);

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
        public string RequestUri { get; private set; } = "";
        public string ApiKeyHeader { get; private set; } = "";
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method.Method;
            RequestUri = request.RequestUri?.ToString() ?? "";
            ApiKeyHeader = request.Headers.TryGetValues("X-API-KEY", out var values) ? values.Single() : "";
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return factory(request, CallCount);
        }
    }
}
