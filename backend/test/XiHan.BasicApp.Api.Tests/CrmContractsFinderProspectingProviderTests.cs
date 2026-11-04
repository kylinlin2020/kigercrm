using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmContractsFinderProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_MapsBuyerAndTenderEvidence()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get,
                "https://www.www.contractsfinder.service.gov.uk/Published/Notices/OCDS/Search"),
            Content = new StringContent(
                """{"releases":[{"ocid":"ocds-h6vhtk-123abc","id":"000123-2026","date":"2026-10-10T10:00:00Z","tender":{"title":"Diesel generator supply","description":"Supply of standby generators","classification":{"description":"Generators"},"value":{"amount":250000,"currency":"GBP"}},"buyer":{"name":"Example Public Authority"},"parties":[{"name":"Example Public Authority","roles":["buyer"],"contactPoint":{"name":"Procurement Desk","email":"procurement@example.gov.uk"},"details":{"url":"https://www.example.gov.uk"}}]}]}""",
                Encoding.UTF8,
                "application/json")
        });
        var provider = new CrmContractsFinderProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            22, "generator procurement", "diesel generator", "United Kingdom", "public authority", "{}", []));

        var record = Assert.Single(result.Records);
        Assert.Equal("uk_contracts_finder", provider.ProviderCode);
        Assert.Equal("CONTRACTSFINDER:ocds-h6vhtk-123abc:000123-2026", record.ExternalId);
        Assert.Equal("Example Public Authority", record.CompanyName);
        Assert.Equal("Procurement Desk", record.ContactName);
        Assert.Equal("procurement@example.gov.uk", record.BusinessEmail);
        Assert.Equal("https://www.example.gov.uk/", record.Website);
        Assert.Equal("official_procurement", record.EvidenceType);
        Assert.Equal("https://www.find-tender.service.gov.uk/Notice/000123-2026", record.SourceUrl);
        Assert.False(string.IsNullOrWhiteSpace(record.RawPayloadHash));
        Assert.Equal(HttpMethod.Get, handler.RequestMethod);
        Assert.Contains("publishedFrom=", handler.RequestUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_RequiresKeywordsBeforeNetwork()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be called."));
        var provider = new CrmContractsFinderProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            22, "", "", "", "", "{}", []));

        Assert.Empty(result.Records);
        Assert.Single(result.Warnings);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_DoesNotTreatSupplierAsBuyer()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get,
                "https://www.www.contractsfinder.service.gov.uk/Published/Notices/OCDS/Search"),
            Content = new StringContent(
                """{"releases":[{"ocid":"ocds-h6vhtk-123abc","id":"000123-2026","tender":{"title":"Generator purchase","description":"Generator supply"},"parties":[{"name":"Supplier Inc","roles":["supplier"]}]}]}""",
                Encoding.UTF8,
                "application/json")
        });
        var provider = new CrmContractsFinderProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            22, "generator", "generator", "", "", "{}", []));

        Assert.Empty(result.Records);
        Assert.Contains(result.Warnings, warning => warning.Contains("buyer name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_FollowsValidatedCursorForAtMostThreePages()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.Query.Contains("cursor=next-page", StringComparison.Ordinal) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, request.RequestUri),
                    Content = new StringContent(
                        """{"releases":[{"ocid":"ocds-h6vhtk-next","id":"000124-2026","tender":{"title":"Water pump purchase","description":"Industrial water pumps"},"buyer":{"name":"Water Authority"}}]}""",
                        Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, request.RequestUri),
                Content = new StringContent(
                    """{"releases":[],"links":{"next":"https://www.www.contractsfinder.service.gov.uk/Published/Notices/OCDS/Search?cursor=next-page"}}""",
                    Encoding.UTF8, "application/json")
            };
        });
        var provider = new CrmContractsFinderProspectingProvider(new HttpClient(handler));

        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(
            22, "water pump", "water pump", "", "", "{}", []));

        Assert.Equal(2, handler.CallCount);
        Assert.Single(result.Records);
        Assert.Contains("cursor=next-page", handler.Requests[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_RejectsPaginationLinkToUnapprovedHost()
    {
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, request.RequestUri),
            Content = new StringContent(
                """{"releases":[],"links":{"next":"https://attacker.example/api/1.0/ocdsReleasePackages?cursor=evil"}}""",
                Encoding.UTF8, "application/json")
        });
        var provider = new CrmContractsFinderProspectingProvider(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.SearchAsync(new CrmProspectingProviderRequest(
                22, "generator", "generator", "", "", "{}", [])));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) => _responseFactory = responseFactory;
        public int CallCount { get; private set; }
        public HttpMethod? RequestMethod { get; private set; }
        public string RequestUri { get; private set; } = string.Empty;
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestMethod = request.Method;
            RequestUri = request.RequestUri?.ToString() ?? string.Empty;
            Requests.Add(RequestUri);
            return Task.FromResult(_responseFactory(request));
        }
    }
}
