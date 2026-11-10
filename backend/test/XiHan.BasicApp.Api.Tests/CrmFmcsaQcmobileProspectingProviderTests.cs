using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmFmcsaQcmobileProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_SkipsNonUsMarketWithoutCallingApi()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Unexpected API call."));
        using var provider = new CrmFmcsaQcmobileProspectingProvider(new HttpClient(handler), "test-web-key");

        var result = await provider.SearchAsync(Request(target: "Nigeria"));

        Assert.Empty(result.Records);
        Assert.True(result.IsComplete);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_RequiresTenantWebKeyBeforeNetworkCall()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Unexpected API call."));
        using var provider = new CrmFmcsaQcmobileProspectingProvider(new HttpClient(handler), (string?)null);

        var result = await provider.SearchAsync(Request());

        Assert.Empty(result.Records);
        Assert.False(result.IsComplete);
        Assert.Equal(1, result.FailureCount);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_UsesFixedEndpointAndMapsOfficialCarrierEvidence()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"content":[{"carrier":{"dotNumber":"1234567","legalName":"Example Generator Logistics LLC","dbaName":"Example Freight","allowedToOperate":"Y","statusCode":"A","phyCountry":"US","phyState":"TX","phyCity":"Houston","phyStreet":"100 Main St","telephone":"555-0100","emailAddress":"fleet@example.com"}}]}"""));
        using var provider = new CrmFmcsaQcmobileProspectingProvider(new HttpClient(handler), "private-web-key");

        var result = await provider.SearchAsync(Request());

        var record = Assert.Single(result.Records);
        Assert.Equal("fmcsa_qcmobile", provider.ProviderCode);
        Assert.Equal("USDOT:1234567", record.ExternalId);
        Assert.Equal("Example Generator Logistics LLC", record.CompanyName);
        Assert.Equal("fleet@example.com", record.BusinessEmail);
        Assert.Equal("registry", record.EvidenceType);
        Assert.Contains("Phone: 555-0100", record.Summary!, StringComparison.Ordinal);
        Assert.StartsWith("https://safer.fmcsa.dot.gov/", record.SourceUrl!, StringComparison.Ordinal);
        Assert.StartsWith("https://mobile.fmcsa.dot.gov/qc/services/carriers/name/", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("webKey=private-web-key", handler.RequestUri, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_MarksMoreThanOneHundredRowsIncomplete()
    {
        var rows = string.Join(",", Enumerable.Range(1, 101).Select(index =>
            $$$"""{"carrier":{"dotNumber":"{{{(7000000 + index)}}}","legalName":"Carrier {{{index}}}"}}"""));
        var handler = new StubHandler((request, _) => Ok(request, $$"""{"content":[{{rows}}]}"""));
        using var provider = new CrmFmcsaQcmobileProspectingProvider(new HttpClient(handler), "test-web-key");

        var result = await provider.SearchAsync(Request());

        Assert.Equal(100, result.Records.Count);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Contains("incomplete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_Propagates429And503Cooldown()
    {
        foreach (var status in new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
        {
            var handler = new StubHandler((request, _) => new HttpResponseMessage(status) { RequestMessage = request });
            using var provider = new CrmFmcsaQcmobileProspectingProvider(new HttpClient(handler), "test-web-key");

            await Assert.ThrowsAsync<CrmProspectingProviderRateLimitException>(() => provider.SearchAsync(Request()));
        }
    }

    private static CrmProspectingProviderRequest Request(string target = "United States")
        => new(1, "find commercial carriers and generator distributors", "generator logistics", target, "fleet operators", "{}", []);

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
