using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Extensions;
using XiHan.BasicApp.Saas.Application.Exporting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmServiceRegistrationTests
{
    [Fact]
    public void AddCrmServices_ShouldRegisterSharedExportProvidersAndKeepProspectingOptIn()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddCrmServices(configuration);

        var exportProviders = services
            .Where(descriptor => descriptor.ServiceType == typeof(IExportProvider))
            .ToArray();

        Assert.Equal(7, exportProviders.Length);
        Assert.All(exportProviders, descriptor =>
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));

        Assert.Empty(services.Where(descriptor =>
            descriptor.ServiceType.Name == "ICrmProspectingProvider"));

        var registry = Assert.Single(services.Where(descriptor =>
            descriptor.ServiceType == typeof(CrmProspectingProviderRegistry)));
        Assert.Equal(ServiceLifetime.Scoped, registry.Lifetime);
    }

    [Fact]
    public void AddCrmServices_ShouldRegisterEveryExplicitlyEnabledProspectingCapability()
    {
        var providerKeys = new[]
        {
            "EnableBraveSearchProvider",
            "EnableBrazilPncpProvider",
            "EnableCompaniesHouseProvider",
            "EnableContractsFinderProvider",
            "EnableFakeProvider",
            "EnableFindTenderProvider",
            "EnableFmcsaQcmobileProvider",
            "EnableFranceCompanySearchProvider",
            "EnableGleifIdentityResolver",
            "EnableHunterDiscoverProvider",
            "EnableKvkProvider",
            "EnableMexicoDenueProvider",
            "EnableNppesProvider",
            "EnableOpenFda510kProvider",
            "EnableRorProvider",
            "EnableSamGovProvider",
            "EnableSecEdgarProvider",
            "EnableSerpApiProvider",
            "EnableSerperProvider",
            "EnableSingaporeGebizProvider",
            "EnableTedProvider",
            "EnableUnComtradeProvider",
            "EnableUsCensusTradeProvider",
            "EnableUsaSpendingProvider",
            "EnableWikidataProvider",
            "EnableWorldBankProcurementProvider"
        };

        var configuration = new ConfigurationManager();
        foreach (var key in providerKeys)
            configuration[$"XiHan:CRM:Prospecting:{key}"] = "true";

        var services = new ServiceCollection();
        services.AddCrmServices(configuration);

        Assert.Equal(25, services.Count(descriptor =>
            descriptor.ServiceType.Name == "ICrmProspectingProvider"));
        Assert.Single(services.Where(descriptor =>
            descriptor.ServiceType.Name == "ICrmProspectingIdentityResolver"));
    }

}
