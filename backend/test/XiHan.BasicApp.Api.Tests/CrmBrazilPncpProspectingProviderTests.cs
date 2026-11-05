using System.Net;
using System.Text;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmBrazilPncpProspectingProviderTests
{
    [Fact]
    public async Task SearchAsync_MapsBrazilianPublicTenderAndBuyerEvidence()
    {
        var handler = new StubHandler(_ => Ok("""{"data":[{"numeroControlePNCP":"12345","objetoCompra":"Aquisição de geradores","dataPublicacaoPncp":"2026-01-02T10:00:00","dataEncerramentoProposta":"2026-02-02","modalidadeNome":"Pregão","valorTotalEstimado":150000,"orgaoEntidade":{"razaoSocial":"Municipality of Recife"},"unidadeOrgao":{"nomeUnidade":"Procurement office","municipioNome":"Recife","ufSigla":"PE"}}],"totalRegistros":1,"totalPaginas":1,"numeroPagina":1}"""));
        using var provider = new CrmBrazilPncpProspectingProvider(new HttpClient(handler));
        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(1,"generator procurement","generator","Brazil","public buyer","{}",[]));
        var record = Assert.Single(result.Records);
        Assert.Equal("brazil_pncp", provider.ProviderCode);
        Assert.Equal("PNCP:12345", record.ExternalId);
        Assert.Equal("Municipality of Recife", record.CompanyName);
        Assert.Equal("official_procurement", record.EvidenceType);
        Assert.True(result.IsComplete);
        Assert.Contains("pagina=1", handler.RequestUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_SkipsWhenBrazilIsNotATargetMarket()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be called."));
        using var provider = new CrmBrazilPncpProspectingProvider(new HttpClient(handler));
        var result = await provider.SearchAsync(new CrmProspectingProviderRequest(1,"generator","generator","Nigeria","","{}",[]));
        Assert.Empty(result.Records);
        Assert.Equal(0, handler.CallCount);
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        RequestMessage = new HttpRequestMessage(HttpMethod.Get,"https://pncp.gov.br/api/consulta/v1/contratacoes/proposta?pagina=1"),
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
