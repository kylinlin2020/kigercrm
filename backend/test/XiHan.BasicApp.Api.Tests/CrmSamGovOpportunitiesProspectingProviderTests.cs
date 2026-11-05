using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmSamGovOpportunitiesProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_RequiresTenantApiKey()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmSamGovOpportunitiesProspectingProvider(new HttpClient(handler), (string?)null);
        var result = await provider.SearchAsync(Request());
        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_MapsOfficialOpportunityAndBuyerEvidence()
    {
        var handler = new StubHandler(_ => Ok("""{"totalRecords":1,"limit":100,"offset":0,"opportunitiesData":[{"noticeId":"notice-123","title":"Supply of water pumps","solicitationNumber":"ABC-123","fullParentPathName":"DEPARTMENT OF ENERGY.WATER OFFICE","postedDate":"2026-10-20","responseDeadLine":"2026-11-20","type":"Solicitation","naicsCode":"333914"}]}"""));
        using var provider = new CrmSamGovOpportunitiesProspectingProvider(new HttpClient(handler), "test-sam-key");
        var result = await provider.SearchAsync(Request());
        var record = Assert.Single(result.Records);
        Assert.Equal("sam_gov", provider.ProviderCode);
        Assert.Equal("SAMGOV:notice-123", record.ExternalId);
        Assert.Equal("DEPARTMENT OF ENERGY.WATER OFFICE", record.CompanyName);
        Assert.Equal("official_procurement", record.EvidenceType);
        Assert.Equal("https://sam.gov/opp/notice-123/view", record.SourceUrl);
        Assert.Contains("api_key=test-sam-key", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("postedFrom=", handler.RequestUri, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    private static CrmProspectingProviderRequest Request()
        => new(1, "pump procurement", "water pumps", "United States", "government buyers", "{}", []);

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.sam.gov/opportunities/v2/search?offset=0"),
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestUri { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? "";
            return Task.FromResult(factory(request));
        }
    }
}
