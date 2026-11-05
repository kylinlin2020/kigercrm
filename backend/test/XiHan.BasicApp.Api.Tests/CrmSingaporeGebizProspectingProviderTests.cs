using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmSingaporeGebizProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_MapsAwardedTenderAsHistoricalBuyerEvidence()
    {
        var handler = new StubHandler(_ => Ok("""{"success":true,"result":{"records":[{"_id":7,"tender_no":"GEBIZ-123","tender_description":"Supply of industrial generators","agency":"Public Utilities Board","award_date":"2026-02-01","tender_detail_status":"Awarded","supplier_name":"Example Power Pte Ltd","awarded_amt":120000}],"total":1,"limit":20}}"""));
        using var provider = new CrmSingaporeGebizProspectingProvider(new HttpClient(handler));
        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(1,"generator procurement","industrial generator","Singapore","public utility","{}",[]));
        var record = Assert.Single(result.Records);
        Assert.Equal("singapore_gebiz", provider.ProviderCode);
        Assert.Equal("GEBIZ:GEBIZ-123:7", record.ExternalId);
        Assert.Equal("Public Utilities Board", record.CompanyName);
        Assert.Equal("official_procurement_award", record.EvidenceType);
        Assert.Contains("Awarded supplier: Example Power Pte Ltd", record.Summary);
        Assert.True(result.IsComplete);
        Assert.Contains("resource_id=", handler.RequestUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_SkipsWhenSingaporeIsNotATargetMarket()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmSingaporeGebizProspectingProvider(new HttpClient(handler));
        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(1,"generator","generator","Nigeria","","{}",[]));
        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        RequestMessage = new HttpRequestMessage(HttpMethod.Get,"https://data.gov.sg/api/action/datastore_search?resource_id=test"),
        Content = new StringContent(json,Encoding.UTF8,"application/json")
    };
    private sealed class StubHandler(Func<HttpRequestMessage,HttpResponseMessage> factory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestUri { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        { CallCount++; RequestUri=request.RequestUri?.ToString() ?? ""; return Task.FromResult(factory(request)); }
    }
}
