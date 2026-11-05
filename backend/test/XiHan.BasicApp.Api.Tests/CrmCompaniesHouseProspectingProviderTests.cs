using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmCompaniesHouseProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_RequiresTenantApiKey()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmCompaniesHouseProspectingProvider(new HttpClient(handler), (string?)null);

        var result = await provider.SearchAsync(Request());

        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_SkipsNonUkMarkets()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmCompaniesHouseProspectingProvider(new HttpClient(handler), "test-key");

        var result = await provider.SearchAsync(Request() with { TargetMarkets = "Nigeria; Ghana" });

        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_UsesBasicAuthAndMapsOfficialCompanyRegistryEvidence()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"total_results":1,"items":[{"company_number":"01234567","title":"Example Generator Holdings Ltd","company_status":"active","company_type":"ltd","description":"42110 - Construction of roads and motorways","address":{"locality":"London","postal_code":"SW1A 1AA","country":"United Kingdom"}}]}"""));
        using var provider = new CrmCompaniesHouseProspectingProvider(new HttpClient(handler), "companies-house-test-key");

        var result = await provider.SearchAsync(Request());
        var record = Assert.Single(result.Records);

        Assert.Equal("companies_house", provider.ProviderCode);
        Assert.Equal("COMPANIESHOUSE:01234567", record.ExternalId);
        Assert.Equal("Example Generator Holdings Ltd", record.CompanyName);
        Assert.Equal("official_company_registry", record.EvidenceType);
        Assert.Equal("https://find-and-update.company-information.service.gov.uk/company/01234567", record.SourceUrl);
        Assert.Contains("London", record.Summary, StringComparison.Ordinal);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("companies-house-test-key:")), handler.Authorization);
        Assert.Contains("q=water%20pumps", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("start_index=0", handler.RequestUri, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_StopsAfterBoundedPagesAndMarksIncompleteCoverage()
    {
        var firstPage = """{"total_results":250,"items":[ITEMS]}""".Replace(
            "ITEMS", string.Join(",", Enumerable.Range(1, 100).Select(i => $$"""{"company_number":"{{i:00000000}}","title":"Generator Company {{i}}","company_status":"active"}""")),
            StringComparison.Ordinal);
        var handler = new StubHandler((request, call) =>
        {
            if (call == 1) return Ok(request, firstPage);
            return Ok(request, """{"total_results":250,"items":[{"company_number":"00000101","title":"Generator Company 101","company_status":"active"}]}""");
        });
        using var provider = new CrmCompaniesHouseProspectingProvider(new HttpClient(handler), "test-key");

        var result = await provider.SearchAsync(Request());

        Assert.Equal(2, handler.CallCount);
        Assert.Contains("start_index=100", handler.SecondRequestUri, StringComparison.Ordinal);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Contains("more matching results", StringComparison.OrdinalIgnoreCase));
    }

    private static CrmProspectingProviderRequest Request()
        => new(1, "find pump distributors", "water pumps", "United Kingdom", "industrial equipment distributors", "{}", []);

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
        public string SecondRequestUri { get; private set; } = "";
        public string Authorization { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? "";
            if (CallCount == 2) SecondRequestUri = RequestUri;
            Authorization = request.Headers.Authorization?.ToString() ?? "";
            return Task.FromResult(factory(request, CallCount));
        }
    }
}
