using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmWorldBankProcurementProviderTests
{
    [Fact]
    public async Task SearchAsync_MapsOfficialNoticeToEvidenceBearingCandidate()
    {
        var handler = new StubHandler(_ => Ok(
            """{"total":1,"procnotices":[{"id":"WB-123","notice_type":"Request for Bids","noticedate":"2026-02-01","submission_deadline_date":"2026-03-01","project_ctry_name":"Nigeria","project_id":"P-1","project_name":"Power infrastructure","bid_reference_no":"NGA-001","bid_description":"Supply of diesel generators","contact_email":"procurement@example.gov","contact_name":"Procurement Officer","contact_organization":"Federal Power Agency"}]}"""));
        using var provider = new CrmWorldBankProcurementProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            17, "generator procurement", "diesel generator", "Nigeria", "power utility", "{}", []));

        Assert.Equal("world_bank_procurement", provider.ProviderCode);
        var record = Assert.Single(result.Records);
        Assert.Equal("WORLD_BANK:WB-123", record.ExternalId);
        Assert.Equal("Federal Power Agency", record.CompanyName);
        Assert.Equal("procurement@example.gov", record.BusinessEmail);
        Assert.Equal("official_procurement", record.EvidenceType);
        Assert.Equal("https://projects.worldbank.org/en/projects-operations/procurement-detail/WB-123", record.SourceUrl);
        Assert.NotNull(record.PublishedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(record.RawPayloadHash));
        Assert.True(result.IsComplete);
        Assert.Empty(result.Warnings);
        Assert.Contains("diesel", handler.RequestUri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpMethod.Get, handler.RequestMethod);
    }

    [Fact]
    public async Task SearchAsync_DoesNotCallNetworkWithoutMeaningfulKeywords()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmWorldBankProcurementProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            17, "", "", "", "", "{}", []));

        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_MarksCoverageIncompleteWhenAdditionalPagesExist()
    {
        var handler = new StubHandler(_ => Ok(
            """{"total":45,"procnotices":[{"id":"WB-1","project_name":"Generator project","bid_description":"Generator supply","contact_organization":"Buyer"}]}"""));
        using var provider = new CrmWorldBankProcurementProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            17, "generator procurement", "generator", "", "", "{}", []));

        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Contains("incomplete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_RejectsUnexpectedResponseEndpoint()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/api/v2/procnotices"),
            Content = new StringContent("""{"total":0,"procnotices":[]}""")
        });
        using var provider = new CrmWorldBankProcurementProvider(new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchAsync(
            new CrmProspectingProviderRequest(17, "generator", "generator", "", "", "{}", [])));
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://search.worldbank.org/api/v2/procnotices?format=json"),
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestUri { get; private set; } = string.Empty;
        public HttpMethod? RequestMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? string.Empty;
            RequestMethod = request.Method;
            return Task.FromResult(responseFactory(request));
        }
    }
}
