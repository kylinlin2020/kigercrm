using System.Net;
using System.Text;
using System.Net.Http.Headers;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmTedProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_MapsBuyerAndOfficialNoticeEvidence()
    {
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, request.RequestUri),
            Content = new StringContent(
                """{"total":1,"results":[{"publication-number":"123456-2026","notice-title":[{"language":"eng","value":"Supply of diesel generators"}],"buyer-name":[{"language":"eng","value":"Example Water Authority"}],"buyer-country":"NGA","publication-date":"20261102","deadline-date":"20261130","form-type":"cn-standard"}]}""",
                Encoding.UTF8, "application/json")
        });
        var provider = new CrmTedProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            22, "generator procurement", "diesel generator", "Nigeria", "public water authority", "{}", []));

        var record = Assert.Single(result.Records);
        Assert.Equal("eu_ted", provider.ProviderCode);
        Assert.Equal("TED:123456-2026", record.ExternalId);
        Assert.Equal("Example Water Authority", record.CompanyName);
        Assert.Equal("Supply of diesel generators", record.Title);
        Assert.Equal("official_procurement", record.EvidenceType);
        Assert.Equal("https://ted.europa.eu/en/notice/-/detail/123456-2026", record.SourceUrl);
        Assert.Equal<DateTimeOffset?>(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), record.PublishedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(record.RawPayloadHash));
        Assert.Equal(HttpMethod.Post, handler.RequestMethod);
        Assert.Contains("/v3/notices/search", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("FT=", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("generator", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("public", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_RequiresKeywordsBeforeNetwork()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be called."));
        var provider = new CrmTedProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            22, "", "", "", "", "{}", []));

        Assert.Empty(result.Records);
        Assert.Single(result.Warnings);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_SkipsNoticeWithoutBuyerOrPublicationNumber()
    {
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, request.RequestUri),
            Content = new StringContent(
                """{"total":1,"results":[{"notice-title":"Generator purchase","buyer-name":" " }]}""",
                Encoding.UTF8, "application/json")
        });
        var provider = new CrmTedProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            22, "generator", "generator", "", "", "{}", []));

        Assert.Empty(result.Records);
        Assert.Contains(result.Warnings, warning => warning.Contains("publication number or buyer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_RejectsChangedEndpoint()
    {
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://attacker.example/v3/notices/search"),
            Content = new StringContent("""{"total":0,"results":[]}""", Encoding.UTF8, "application/json")
        });
        var provider = new CrmTedProspectingProvider(new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.SearchAsync(new CrmProspectingProviderRequest(
                22, "generator", "generator", "", "", "{}", [])));
    }

    [Fact]
    public async Task SearchAsync_EmitsStructuredRateLimitWithRetryAfter()
    {
        var handler = new StubHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Post, request.RequestUri)
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return response;
        });
        var provider = new CrmTedProspectingProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<CrmProspectingProviderRateLimitException>(() =>
            provider.SearchAsync(new CrmProspectingProviderRequest(
                22, "generator", "generator", "", "", "{}", [])));

        Assert.Equal(429, exception.HttpStatus);
        Assert.NotNull(exception.RetryAfterAt);
        Assert.InRange(exception.RetryAfterAt.Value, DateTimeOffset.UtcNow.AddMinutes(1), DateTimeOffset.UtcNow.AddMinutes(3));
    }

    [Fact]
    public async Task SearchAsync_UsesCoverageLowerBoundInPublicationDateQuery()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"total":0,"results":[]}""", Encoding.UTF8, "application/json")
        });
        var provider = new CrmTedProspectingProvider(new HttpClient(handler));
        var lowerBound = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            22, "water pump", "water pump", "", "", "{}", [])
        {
            CoverageSinceUtc = lowerBound
        });

        Assert.Single(handler.RequestBody);
        Assert.Contains("PD=(20261020", handler.RequestBody, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) => _responseFactory = responseFactory;
        public int CallCount { get; private set; }
        public HttpMethod? RequestMethod { get; private set; }
        public string RequestUri { get; private set; } = string.Empty;
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestMethod = request.Method;
            RequestUri = request.RequestUri?.ToString() ?? string.Empty;
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responseFactory(request);
        }
    }
}