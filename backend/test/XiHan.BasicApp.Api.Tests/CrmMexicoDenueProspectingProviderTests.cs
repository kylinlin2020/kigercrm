using System.Net;
using System.Text;
using System.Text.Json;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmMexicoDenueProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_MapsOfficialBusinessAndKeepsTokenOnlyInRequestPath()
    {
        const string token = "private-denue-token";
        var handler = new StubHandler((request, _) => JsonResponse(request,
            """[{"Id":"123","Nombre":"Generator Supply","Razon_social":"Generator Supply SA","Clase_actividad":"Wholesale generators","Estrato":"Small","Calle":"Reforma","Municipio":"Monterrey","Entidad":"Nuevo Leon","Telefono":"5551234","Correo_e":"sales@example.mx","Sitio_internet":"https://example.mx"}]"""));
        using var provider = new CrmMexicoDenueProspectingProvider(new HttpClient(handler), token);

        var result = await provider.SearchAsync(Request("Mexico"));

        var record = Assert.Single(result.Records);
        Assert.Equal("mexico_denue", provider.ProviderCode);
        Assert.Equal("DENUE:123", record.ExternalId);
        Assert.Equal("Generator Supply SA", record.CompanyName);
        Assert.Equal("sales@example.mx", record.BusinessEmail);
        Assert.Equal("official_business_registry", record.EvidenceType);
        Assert.Equal("https://example.mx/", record.Website);
        Assert.Contains("/Buscar/generator/00/1/20/private-denue-token", handler.RequestUri, StringComparison.Ordinal);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_SkipsNonMexicoAndDoesNotCallNetwork()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmMexicoDenueProspectingProvider(new HttpClient(handler), "token");
        var result = await provider.SearchAsync(Request("Nigeria"));
        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_RequiresTenantToken()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmMexicoDenueProspectingProvider(new HttpClient(handler), (string?)null);
        var result = await provider.SearchAsync(Request("Mexico"));
        Assert.Equal(1, result.FailureCount);
        Assert.Equal(0, handler.CallCount);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task SearchAsync_StopsAtOneHundredRecordsAndMarksCoverageIncomplete()
    {
        var rows = Enumerable.Range(1, 20).Select(id => new { Id = id.ToString(), Nombre = "Generator Company " + id, Clase_actividad = "Generator wholesale" });
        var json = JsonSerializer.Serialize(rows);
        var handler = new StubHandler((request, _) => JsonResponse(request, json));
        using var provider = new CrmMexicoDenueProspectingProvider(new HttpClient(handler), "token");

        var result = await provider.SearchAsync(Request("Mexico"));

        Assert.Equal(100, result.Records.Count);
        Assert.Equal(5, handler.CallCount);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Contains("incomplete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_Maps429ToCooldown()
    {
        var handler = new StubHandler((request, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            RequestMessage = request,
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        using var provider = new CrmMexicoDenueProspectingProvider(new HttpClient(handler), "token");
        await Assert.ThrowsAsync<CrmProspectingProviderRateLimitException>(() => provider.SearchAsync(Request("Mexico")));
    }

    private static CrmProspectingProviderRequest Request(string market)
        => new(1, "generator suppliers", "generator", market, "industrial generator buyer", "{}", []);

    private static HttpResponseMessage JsonResponse(HttpRequestMessage request, string json)
        => new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestUri { get; private set; } = string.Empty;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.ToString() ?? string.Empty;
            return Task.FromResult(factory(request, cancellationToken));
        }
    }
}
