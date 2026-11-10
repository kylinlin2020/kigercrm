using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Source-level contract guards for the provider catalog endpoint. These intentionally read files
/// instead of reflecting over assemblies so the guard also covers the frontend typed client.
/// </summary>
public sealed class CrmProspectingProviderCatalogContractTests
{
    private static readonly string[] ExpectedCodes =
    [
        "serper", "brave", "serpapi", "companies_house", "kvk", "fr_company_search", "sec_edgar",
        "ror", "wikidata", "nppes", "open_fda_510k", "eu_ted", "uk_find_tender",
        "uk_contracts_finder", "usaspending_awards", "world_bank_procurement", "brazil_pncp",
        "singapore_gebiz", "sam_gov", "un_comtrade", "us_census_trade", "hunter",
        "mexico_denue", "fmcsa_qcmobile",
    ];

    [Fact]
    public void Catalog_ShouldListEveryRealProviderCode()
    {
        var catalog = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Prospecting", "CrmProspectingProviderCatalog.cs");
        foreach (var code in ExpectedCodes)
            Assert.Contains($"new(\"{code}\"", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("new(\"fake\"", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogEndpoint_ShouldMergeStaticMetadataWithRuntimeRegistry()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmProspectingProviderCatalogQueryService.cs");
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Contracts", "ICrmProspectingProviderCatalogQueryService.cs");
        var dtos = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmProspectingProviderCatalogDtos.cs");

        Assert.Contains("GetProvidersAsync", contract, StringComparison.Ordinal);
        Assert.Contains("GetProvidersAsync", service, StringComparison.Ordinal);
        Assert.Contains("[DynamicApi(", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", service, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingProviderRegistry registry", service, StringComparison.Ordinal);
        Assert.Contains("registry.Synchronous.ContainsKey", service, StringComparison.Ordinal);
        Assert.Contains("registry.Asynchronous.ContainsKey", service, StringComparison.Ordinal);
        Assert.Contains("IsExecutable", dtos, StringComparison.Ordinal);
        Assert.Contains("ConfigKey", dtos, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendClient_ShouldUseTheCatalogDynamicApiRoute()
    {
        var client = Read("frontend", "src", "api", "modules", "crm", "prospecting.ts");
        Assert.Contains("createDynamicApiClient('CrmProspectingProviderCatalogQuery')", client, StringComparison.Ordinal);
        Assert.Contains("'Providers'", client, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
