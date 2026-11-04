using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmUsaSpendingProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_MapsOfficialAwardToEvidenceBearingCandidateRecord()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post,
                "https://api.usaspending.gov/api/v2/search/spending_by_award/"),
            Content = new StringContent(
                """{"page_metadata":{"page":1,"hasNext":false},"results":[{"Award ID":"ABC-123","Recipient Name":"Example Generator Supply","Award Amount":125000,"Description":"Generator procurement","Start Date":"2025-01-01","End Date":"2025-12-31","Awarding Agency":"Example Agency","generated_internal_id":"award-ref-1"}]}""",
                Encoding.UTF8,
                "application/json")
        });
        var provider = new CrmUsaSpendingProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            17, "generator supplier", "diesel generator", "United States", "industrial distributor", "{}", []));

        Assert.Equal("usaspending_awards", provider.ProviderCode);
        var record = Assert.Single(result.Records);
        Assert.Equal("USASPENDING:ABC-123", record.ExternalId);
        Assert.Equal("Example Generator Supply", record.CompanyName);
        Assert.Equal("official_procurement", record.EvidenceType);
        Assert.Equal("https://www.usaspending.gov/award/award-ref-1/", record.SourceUrl);
        Assert.False(string.IsNullOrWhiteSpace(record.RawPayloadHash));
        Assert.Empty(result.Warnings);
        Assert.Equal(0, result.FailureCount);
        Assert.Contains("diesel", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpMethod.Post, handler.RequestMethod);
        Assert.Equal("https://api.usaspending.gov/api/v2/search/spending_by_award/", handler.RequestUri);
    }

    [Fact]
    public async Task SearchAsync_DoesNotCallNetworkWithoutUsefulKeywords()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be called."));
        var provider = new CrmUsaSpendingProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            17, "", "", "", "", "{}", []));

        Assert.Empty(result.Records);
        Assert.Single(result.Warnings);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_RejectsNonSuccessStatusWithoutLeakingResponseBody()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post,
                "https://api.usaspending.gov/api/v2/search/spending_by_award/"),
            Content = new StringContent("sensitive-provider-response")
        });
        var provider = new CrmUsaSpendingProspectingProvider(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<CrmProspectingProviderRateLimitException>(() =>
            provider.SearchAsync(new CrmProspectingProviderRequest(
                17, "generator", "generator", "", "", "{}", [])));

        Assert.Equal(429, error.HttpStatus);
        Assert.NotNull(error.RetryAfterAt);
        Assert.True(error.RetryAfterAt > DateTimeOffset.UtcNow);
        Assert.DoesNotContain("sensitive-provider-response", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-provider-response", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_RejectsResponseLargerThanSixMiB()
    {
        var oversized = new string('x', 6 * 1024 * 1024 + 1);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post,
                "https://api.usaspending.gov/api/v2/search/spending_by_award/"),
            Content = new StringContent(oversized, Encoding.UTF8, "application/json")
        });
        var provider = new CrmUsaSpendingProspectingProvider(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.SearchAsync(new CrmProspectingProviderRequest(
                17, "generator", "generator", "", "", "{}", [])));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int CallCount { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;
        public HttpMethod? RequestMethod { get; private set; }
        public string? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestMethod = request.Method;
            RequestUri = request.RequestUri?.ToString();
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responseFactory(request);
        }
    }
}
