using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmKvkProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_RequiresTenantApiKey()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmKvkProspectingProvider(new HttpClient(handler), (string?)null);

        var result = await provider.SearchAsync(Request());

        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_SkipsNonDutchMarkets()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmKvkProspectingProvider(new HttpClient(handler), "test-key");

        var result = await provider.SearchAsync(Request() with { TargetMarkets = "Belgium; Germany" });

        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_UsesApiKeyHeaderAndMapsOfficialRegistryEvidence()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"pagina":1,"resultatenPerPagina":100,"totaal":1,"resultaten":[{"kvkNummer":"12345678","naam":"Example Pump Systems B.V.","actief":"Ja","type":"Rechtspersoon","vestigingsnummer":"000012345678","adres":{"straatnaam":"Industrieweg","huisnummer":"12","postcode":"1234 AB","plaats":"Rotterdam","land":"Nederland"}}]}"""));
        using var provider = new CrmKvkProspectingProvider(new HttpClient(handler), "kvk-test-key");

        var result = await provider.SearchAsync(Request() with
        {
            ProductScope = "water",
            IdealCustomerProfile = "",
            SearchObjective = ""
        });
        var record = Assert.Single(result.Records);

        Assert.Equal("kvk", provider.ProviderCode);
        Assert.Equal("KVK:12345678", record.ExternalId);
        Assert.Equal("Example Pump Systems B.V.", record.CompanyName);
        Assert.Equal("official_company_registry", record.EvidenceType);
        Assert.Contains("Rotterdam", record.Summary!, StringComparison.Ordinal);
        Assert.Equal("kvk-test-key", handler.ApiKey);
        Assert.Contains("naam=water", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("pagina=1", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("resultatenPerPagina=100", handler.RequestUri, StringComparison.Ordinal);
        Assert.DoesNotContain("kvk-test-key", handler.RequestUri, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_StopsAtBoundedPageCountAndMarksIncompleteCoverage()
    {
        var firstPage = """{"totaal":250,"resultaten":[ITEMS]}""".Replace(
            "ITEMS",
            string.Join(",", Enumerable.Range(1, 100).Select(i =>
                $$"""{"kvkNummer":"{{i:00000000}}","naam":"Pump Company {{i}}","actief":"Ja"}""")),
            StringComparison.Ordinal);
        var handler = new StubHandler((request, call) => call == 1
            ? Ok(request, firstPage)
            : Ok(request, """{"totaal":250,"resultaten":[{"kvkNummer":"00000101","naam":"Pump Company 101","actief":"Ja"}]}"""));
        using var provider = new CrmKvkProspectingProvider(new HttpClient(handler), "test-key");

        var result = await provider.SearchAsync(Request() with
        {
            ProductScope = "water",
            IdealCustomerProfile = "",
            SearchObjective = ""
        });

        Assert.Equal(2, handler.CallCount);
        Assert.Contains("pagina=2", handler.SecondRequestUri, StringComparison.Ordinal);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Contains("more matches", StringComparison.OrdinalIgnoreCase));
    }

    private static CrmProspectingProviderRequest Request()
        => new(1, "find pump distributors", "water pumps", "Netherlands", "industrial equipment distributors", "{}", []);

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
        public string ApiKey { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? "";
            if (CallCount == 2) SecondRequestUri = RequestUri;
            ApiKey = request.Headers.TryGetValues("apikey", out var values) ? values.Single() : "";
            return Task.FromResult(factory(request, CallCount));
        }
    }
}
