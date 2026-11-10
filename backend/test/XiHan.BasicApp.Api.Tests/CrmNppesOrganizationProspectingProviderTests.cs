using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmNppesOrganizationProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_SkipsNonHealthcareCampaignWithoutCallingApi()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Unexpected API call."));
        using var provider = new CrmNppesOrganizationProspectingProvider(new HttpClient(handler));
        var result = await provider.SearchAsync(Request("industrial generator suppliers", "power equipment distributors"));

        Assert.Empty(result.Records);
        Assert.True(result.IsComplete);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_SkipsNonUsMarketWithoutCallingApi()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Unexpected API call."));
        using var provider = new CrmNppesOrganizationProspectingProvider(new HttpClient(handler));
        var result = await provider.SearchAsync(Request(target: "Canada"));

        Assert.Empty(result.Records);
        Assert.True(result.IsComplete);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_OnlyMapsNpiType2OrganizationRecords()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"result_count":2,"results":[{"enumeration_type":"NPI-1","number":"1234567890","basic":{"first_name":"Jane","last_name":"Doe"}},{"enumeration_type":"NPI-2","number":"1234567891","basic":{"organization_name":"Example Medical Center","status":"A","last_updated":"2026-01-15"},"taxonomies":[{"code":"261Q00000X","desc":"Clinic/Center","primary":true}],"addresses":[{"address_purpose":"LOCATION","city":"Austin","state":"TX","postal_code":"78701"}]}]}"""));
        using var provider = new CrmNppesOrganizationProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(Request());

        var record = Assert.Single(result.Records);
        Assert.Equal("nppes", provider.ProviderCode);
        Assert.Equal("NPPES:1234567891", record.ExternalId);
        Assert.Equal("Example Medical Center", record.CompanyName);
        Assert.Equal("us_healthcare_organization_registry", record.EvidenceType);
        Assert.Contains("NPI Type 2", record.Summary!, StringComparison.Ordinal);
        Assert.Contains("enumeration_type=NPI-2", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("limit=200", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("skip=0", handler.RequestUri, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_UsesOnlyBoundedTwoPagesAndMarksFullSecondPageIncomplete()
    {
        var handler = new StubHandler((request, call) =>
        {
            var entries = string.Join(",", Enumerable.Range(1, 200)
                .Select(i => $$$"""{"enumeration_type":"NPI-2","number":"{{{(1000000000 + call * 200 + i).ToString()}}}","basic":{"organization_name":"Medical Center {{{call}}}-{{{i}}}","status":"A"}}"""));
            return Ok(request, $$"""{"result_count":200,"results":[{{entries}}]}""");
        });
        using var provider = new CrmNppesOrganizationProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(Request(product: "medical equipment", profile: "hospitals clinics"));

        Assert.Equal(4, handler.CallCount);
        Assert.Equal(800, result.Records.Count);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Contains("bounded 2-page window", StringComparison.Ordinal));
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
        using var provider = new CrmNppesOrganizationProspectingProvider(new HttpClient(handler));

        await Assert.ThrowsAsync<CrmProspectingProviderRateLimitException>(() => provider.SearchAsync(Request()));
    }

    private static CrmProspectingProviderRequest Request(
        string product = "medical equipment",
        string profile = "healthcare clinics",
        string target = "United States")
        => new(1, "find medical organizations", product, target, profile, "{}", []);

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
