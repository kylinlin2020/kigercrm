using System.Net;
using System.Text;
using System.Net.Http.Headers;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmGleifIdentityResolverTests
{
    [Fact]
    public async Task ResolveExactAsync_ReturnsOnlyUniqueExactLegalNameMatch()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.gleif.org/api/v1/lei-records"),
            Content = new StringContent(
                """{"meta":{"pagination":{"total":1}},"data":[{"type":"lei-records","id":"549300NYKK9MWM7GGW15","attributes":{"entity":{"legalName":{"name":"Example Generator Holdings Ltd"},"status":"ACTIVE","registeredAs":"123456","legalAddress":{"country":"GB"}},"registration":{"status":"ISSUED"}}}]}""",
                Encoding.UTF8, "application/vnd.api+json")
        });
        var resolver = new CrmGleifIdentityResolver(new HttpClient(handler));

        var match = await resolver.ResolveExactAsync("Example Generator Holdings Ltd");

        Assert.NotNull(match);
        Assert.Equal("549300NYKK9MWM7GGW15", match.Lei);
        Assert.Equal("Example Generator Holdings Ltd", match.LegalName);
        Assert.Equal("ACTIVE", match.EntityStatus);
        Assert.Equal("ISSUED", match.RegistrationStatus);
        Assert.Equal("GB", match.CountryCode);
        Assert.Equal("123456", match.RegisteredAs);
        Assert.Contains("/lei-records/549300NYKK9MWM7GGW15", match.SourceUrl, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(match.RawPayloadHash));
        Assert.Contains("filter%5Bentity.legalName%5D=", handler.RequestUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveExactAsync_RejectsAmbiguousLegalNameMatches()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.gleif.org/api/v1/lei-records"),
            Content = new StringContent(
                """{"meta":{"pagination":{"total":2}},"data":[{"id":"549300NYKK9MWM7GGW15","attributes":{"entity":{"legalName":{"name":"Example Generator Holdings Ltd"}}}},{"id":"213800D1EI4B9WTWWD28","attributes":{"entity":{"legalName":{"name":"Example Generator Holdings Ltd"}}}}]}""",
                Encoding.UTF8, "application/vnd.api+json")
        });
        var resolver = new CrmGleifIdentityResolver(new HttpClient(handler));

        var match = await resolver.ResolveExactAsync("Example Generator Holdings Ltd");

        Assert.Null(match);
    }

    [Fact]
    public async Task ResolveExactAsync_FailsClosedWhenPaginationMetadataIsMissing()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.gleif.org/api/v1/lei-records"),
            Content = new StringContent(
                """{"data":[{"id":"549300NYKK9MWM7GGW15","attributes":{"entity":{"legalName":{"name":"Example Generator Holdings Ltd"}}}}]}""",
                Encoding.UTF8, "application/vnd.api+json")
        });
        var resolver = new CrmGleifIdentityResolver(new HttpClient(handler));

        var match = await resolver.ResolveExactAsync("Example Generator Holdings Ltd");

        Assert.Null(match);
    }

    [Fact]
    public async Task ResolveExactAsync_DoesNotCallNetworkForShortOrBlankNames()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be called."));
        var resolver = new CrmGleifIdentityResolver(new HttpClient(handler));

        Assert.Null(await resolver.ResolveExactAsync("ABC"));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ResolveExactAsync_EmitsStructuredRateLimitWithRetryAfter()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.gleif.org/api/v1/lei-records")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return response;
        });
        var resolver = new CrmGleifIdentityResolver(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<CrmProspectingProviderRateLimitException>(() =>
            resolver.ResolveExactAsync("Example Generator Holdings Ltd"));

        Assert.Equal(429, exception.HttpStatus);
        Assert.NotNull(exception.RetryAfterAt);
        Assert.InRange(exception.RetryAfterAt.Value, DateTimeOffset.UtcNow.AddMinutes(1), DateTimeOffset.UtcNow.AddMinutes(3));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;
        public int CallCount { get; private set; }
        public string RequestUri { get; private set; } = string.Empty;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? string.Empty;
            return Task.FromResult(_factory(request));
        }
    }
}
