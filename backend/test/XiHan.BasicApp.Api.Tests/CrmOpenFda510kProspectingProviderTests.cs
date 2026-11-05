using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmOpenFda510kProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_SkipsNonMedicalCampaignsWithoutCallingApi()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Unexpected API call."));
        using var provider = new CrmOpenFda510kProspectingProvider(new HttpClient(handler), "test-key");

        var result = await provider.SearchAsync(Request(product: "industrial water pumps", profile: "pump distributors"));

        Assert.Empty(result.Records);
        Assert.True(result.IsComplete);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_Maps510kApplicantAndDeviceAsRegulatoryEvidence()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"meta":{"results":{"total":1}},"results":[{"k_number":"K241234","device_name":"Diagnostic Imaging System","applicant":"Example Medical Inc","decision_date":"20240131","product_code":"JAK"}]}"""));
        using var provider = new CrmOpenFda510kProspectingProvider(new HttpClient(handler), "tenant-secret");

        var result = await provider.SearchAsync(Request());

        var record = Assert.Single(result.Records);
        Assert.Equal("open_fda_510k", provider.ProviderCode);
        Assert.Equal("FDA510K:K241234", record.ExternalId);
        Assert.Equal("Example Medical Inc", record.CompanyName);
        Assert.Equal("Diagnostic Imaging System", record.Title);
        Assert.Equal("medical_device_regulatory_record", record.EvidenceType);
        Assert.Contains("K241234", record.SourceUrl!, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-secret", record.SourceUrl!, StringComparison.Ordinal);
        Assert.Contains("api_key=tenant-secret", handler.RequestUri, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_UsesBoundedPagesAndMarksCappedCoverageIncomplete()
    {
        var entries = string.Join(",", Enumerable.Range(1, 100)
            .Select(i => $$"""{"k_number":"K24{{i:D4}}","device_name":"Medical Pump {{i}}","applicant":"Applicant {{i}}","decision_date":"20240101"}"""));
        var handler = new StubHandler((request, _) => Ok(request,
            $$"""{"meta":{"results":{"total":500}},"results":[{{entries}}]}"""));
        using var provider = new CrmOpenFda510kProspectingProvider(new HttpClient(handler), null);

        var result = await provider.SearchAsync(Request(product: "medical pump"));

        Assert.Equal(3, handler.CallCount);
        Assert.Equal(300, result.Records.Count);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Contains("only 300 were read", StringComparison.Ordinal));
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
        using var provider = new CrmOpenFda510kProspectingProvider(new HttpClient(handler), null);

        await Assert.ThrowsAsync<CrmProspectingProviderRateLimitException>(() => provider.SearchAsync(Request()));
    }

    private static CrmProspectingProviderRequest Request(string product = "medical device imaging", string profile = "medical equipment manufacturers")
        => new(1, "medical device", product, "US", profile == "medical equipment manufacturers" ? "medical device" : profile, "{}", []);

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
