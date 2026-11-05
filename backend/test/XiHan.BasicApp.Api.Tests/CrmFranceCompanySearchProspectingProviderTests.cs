using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmFranceCompanySearchProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_SkipsNonFrenchMarkets()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmFranceCompanySearchProspectingProvider(new HttpClient(handler));
        var result = await provider.SearchAsync(Request() with { TargetMarkets = "Germany" });
        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_MapsFrenchOfficialRegistryEvidenceAndLimitsFields()
    {
        var handler = new StubHandler((request, _) => Ok(request,
            """{"total_pages":1,"results":[{"siren":"123456789","nom_complet":"Example Pompes SAS","etat_administratif":"A","activite_principale":"28.13Z","siege":{"siret":"12345678900012","code_postal":"75001","libelle_commune":"Paris"}}]}"""));
        using var provider = new CrmFranceCompanySearchProspectingProvider(new HttpClient(handler));
        var result = await provider.SearchAsync(Request());
        var record = Assert.Single(result.Records);

        Assert.Equal("fr_company_search", provider.ProviderCode);
        Assert.Equal("FR-SIREN:123456789", record.ExternalId);
        Assert.Equal("Example Pompes SAS", record.CompanyName);
        Assert.Equal("official_company_registry", record.EvidenceType);
        Assert.Equal("https://annuaire-entreprises.data.gouv.fr/entreprise/123456789", record.SourceUrl);
        Assert.Contains("Paris", record.Summary!, StringComparison.Ordinal);
        Assert.Contains("per_page=25", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("minimal=true", handler.RequestUri, StringComparison.Ordinal);
        Assert.True(handler.UserAgent.Contains("KigerCRM-Prospecting", StringComparison.Ordinal));
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_StopsAtBoundedPageAndMarksRemainingResultsIncomplete()
    {
        var handler = new StubHandler((request, call) => Ok(request,
            $$"""{"total_pages":4,"results":[{"siren":"12345678{{call}}","nom_complet":"Pump Supplier {{call}}","siege":{"libelle_commune":"Lyon"}}]}"""));
        using var provider = new CrmFranceCompanySearchProspectingProvider(new HttpClient(handler));
        var result = await provider.SearchAsync(Request());

        Assert.Equal(2, handler.CallCount);
        Assert.Contains("page=2", handler.SecondRequestUri, StringComparison.Ordinal);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Contains("more pages", StringComparison.OrdinalIgnoreCase));
    }

    private static CrmProspectingProviderRequest Request()
        => new(1, "find pump manufacturers", "water pumps", "France", "industrial equipment distributors", "{}", []);

    private static HttpResponseMessage Ok(HttpRequestMessage request, string json)
        => new(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestUri { get; private set; } = "";
        public string SecondRequestUri { get; private set; } = "";
        public string UserAgent { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? "";
            if (CallCount == 2) SecondRequestUri = RequestUri;
            UserAgent = string.Join(" ", request.Headers.UserAgent);
            return Task.FromResult(factory(request, CallCount));
        }
    }
}
