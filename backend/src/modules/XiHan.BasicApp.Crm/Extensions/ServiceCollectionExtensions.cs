// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;
using XiHan.BasicApp.Crm.Infrastructure.Seeders;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.Extensions.DependencyInjection;
using XiHan.BasicApp.Saas.Extensions;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Crm.Extensions;

/// <summary>
/// Service registration helpers for the KIGER CRM module.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CRM entities with the platform field-security/entity metadata system.
    /// </summary>
    public static IServiceCollection AddCrmFieldSecurityEntities(this IServiceCollection services)
    {
        return services.AddFieldSecurityEntities(entities => entities
            .Add<SysCrmCustomer>()
            .Add<SysCrmContact>()
            .Add<SysCrmLead>()
            .Add<SysCrmOpportunity>()
            .Add<SysCrmActivity>()
            .Add<SysCrmTask>()
            .Add<SysCrmQuote>()
            .Add<SysCrmQuoteLine>()
            .Add<SysCrmSalesOrder>()
            .Add<SysCrmSalesOrderLine>()
            .Add<SysCrmShipment>()
            .Add<SysCrmShipmentLine>()
            .Add<SysCrmProspectingCampaign>()
            .Add<SysCrmProspectingStrategy>()
            .Add<SysCrmProspectingRun>()
            .Add<SysCrmProspectingCandidate>()
            .Add<SysCrmProspectingEvidence>()
            .Add<SysCrmProspectingScore>()
            .Add<SysCrmProspectingProviderRequest>()
            .Add<SysCrmProspectingProviderCoverage>());
        
    }

    /// <summary>Registers CRM services and seeders; synthetic prospecting is opt-in for local smoke tests only.</summary>
    public static IServiceCollection AddCrmDataSeeders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddScoped<ICrmProspectingCampaignRepository, CrmProspectingCampaignRepository>();
        services.AddScoped<ICrmProspectingStrategyRepository, CrmProspectingStrategyRepository>();
        services.AddScoped<ICrmProspectingRunRepository, CrmProspectingRunRepository>();
        services.AddScoped<ICrmProspectingCandidateRepository, CrmProspectingCandidateRepository>();
        services.AddScoped<ICrmProspectingEvidenceRepository, CrmProspectingEvidenceRepository>();
        services.AddScoped<ICrmProspectingScoreRepository, CrmProspectingScoreRepository>();
        services.AddScoped<ICrmProspectingProviderRequestRepository, CrmProspectingProviderRequestRepository>();
        // CrmProspectingRunHostedService follows the framework worker convention:
        // XiHanBackgroundServiceBase supplies IBackgroundWorker/SingletonDependency and the HostedService suffix
        // is auto-exposed as IHostedService. Do not call AddHostedService here (it would double-consume the queue).
        services.AddScoped<ICrmProspectingDomainService, CrmProspectingDomainService>();
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableFakeProvider"], out var enableFakeProvider)
            && enableFakeProvider)
        {
            services.AddSingleton<ICrmProspectingProvider, FakeCrmProspectingProvider>();
        }

        // Official public procurement data is opt-in. Redirects are disabled because provider
        // endpoints are fixed and must never be able to redirect the worker to an arbitrary host.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableUsaSpendingProvider"], out var enableUsaSpendingProvider)
            && enableUsaSpendingProvider)
        {
            services.AddSingleton<CrmUsaSpendingProspectingProvider>(provider => new CrmUsaSpendingProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("api.usaspending.gov"))
                {
                    Timeout = TimeSpan.FromSeconds(25)
                }));
            services.AddSingleton<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmUsaSpendingProspectingProvider>());
        }
        // Brazil PNCP public procurement API is enabled only by explicit configuration.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableBrazilPncpProvider"], out var enableBrazilPncp)
            && enableBrazilPncp)
        {
            services.AddSingleton<CrmBrazilPncpProspectingProvider>(provider => new CrmBrazilPncpProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("pncp.gov.br"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                }));
            services.AddSingleton<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmBrazilPncpProspectingProvider>());
        }
        // GeBIZ award dataset is public data.gov.sg content; it is explicitly opt-in.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableSingaporeGebizProvider"], out var enableSingaporeGebiz)
            && enableSingaporeGebiz)
        {
            services.AddSingleton<CrmSingaporeGebizProspectingProvider>(provider => new CrmSingaporeGebizProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("data.gov.sg"))
                {
                    Timeout = TimeSpan.FromSeconds(25)
                }));
            services.AddSingleton<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmSingaporeGebizProspectingProvider>());
        }
        // World Bank procurement notices are public data. The fixed official endpoint is
        // opt-in and uses the same DNS-pinned, redirect-disabled outbound boundary.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableWorldBankProcurementProvider"], out var enableWorldBankProvider)
            && enableWorldBankProvider)
        {
            services.AddSingleton<CrmWorldBankProcurementProvider>(provider => new CrmWorldBankProcurementProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("search.worldbank.org"))
                {
                    Timeout = TimeSpan.FromSeconds(25)
                }));
            services.AddSingleton<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmWorldBankProcurementProvider>());
        }
        // Official UK procurement notices are public OCDS data. The adapter is opt-in and
        // uses a fixed endpoint with redirects disabled.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableFindTenderProvider"], out var enableFindTenderProvider)
            && enableFindTenderProvider)
        {
            services.AddSingleton<CrmFindTenderProspectingProvider>(provider => new CrmFindTenderProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("www.find-tender.service.gov.uk"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                }));
            services.AddSingleton<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmFindTenderProspectingProvider>());
        }
        // Contracts Finder publishes public UK procurement notices as OCDS data.
        // This separate opt-in adapter complements Find a Tender and does not infer supplier-as-buyer.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableContractsFinderProvider"], out var enableContractsFinderProvider)
            && enableContractsFinderProvider)
        {
            services.AddSingleton<CrmContractsFinderProspectingProvider>(provider => new CrmContractsFinderProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("www.contractsfinder.service.gov.uk"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                }));
            services.AddSingleton<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmContractsFinderProspectingProvider>());
        }
        // EU TED publishes open procurement notices through its anonymous Search API.
        // The adapter is opt-in and uses a fixed HTTPS endpoint with redirects disabled.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableTedProvider"], out var enableTedProvider)
            && enableTedProvider)
        {
            services.AddSingleton<CrmTedProspectingProvider>(provider => new CrmTedProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("api.ted.europa.eu"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                }));
            services.AddSingleton<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmTedProspectingProvider>());
        }
        // SAM.gov public opportunities require a tenant-specific API key and remain explicitly opt-in.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableSamGovProvider"], out var enableSamGov)
            && enableSamGov)
        {
            services.AddScoped<CrmSamGovOpportunitiesProspectingProvider>(provider => new CrmSamGovOpportunitiesProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("api.sam.gov"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                },
                provider.GetRequiredService<ISaasConfigurationService>()));
            services.AddScoped<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmSamGovOpportunitiesProspectingProvider>());
        }
        // openFDA 510(k) is a medical-device regulatory signal only; credentials are tenant-scoped and optional.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableOpenFda510kProvider"], out var enableOpenFda510k)
            && enableOpenFda510k)
        {
            services.AddScoped<CrmOpenFda510kProspectingProvider>(provider => new CrmOpenFda510kProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("api.fda.gov"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                },
                provider.GetRequiredService<ISaasConfigurationService>()));
            services.AddScoped<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmOpenFda510kProspectingProvider>());
        }
        // Wikidata is a public knowledge graph. Keep results explicitly classified as discovery clues,
        // not authoritative company registration or buyer-intent evidence.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableWikidataProvider"], out var enableWikidata)
            && enableWikidata)
        {
            services.AddSingleton<CrmWikidataProspectingProvider>(provider => new CrmWikidataProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("www.wikidata.org"))
                {
                    Timeout = TimeSpan.FromSeconds(35)
                }));
            services.AddSingleton<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmWikidataProspectingProvider>());
        }
        // ROR is a public identifier registry for research organizations; it is not a general company directory.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableRorProvider"], out var enableRor)
            && enableRor)
        {
            services.AddSingleton<CrmRorProspectingProvider>(provider => new CrmRorProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("api.ror.org"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                }));
            services.AddSingleton<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmRorProspectingProvider>());
        }
        // Companies House is the UK official company register. Search results are registry evidence,
        // not proof of buyer intent; the API key is read from the current tenant's encrypted configuration.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableCompaniesHouseProvider"], out var enableCompaniesHouse)
            && enableCompaniesHouse)
        {
            services.AddScoped<CrmCompaniesHouseProspectingProvider>(provider => new CrmCompaniesHouseProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("api.company-information.service.gov.uk"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                },
                provider.GetRequiredService<ISaasConfigurationService>()));
            services.AddScoped<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmCompaniesHouseProspectingProvider>());
        }
        // French public business-directory search is open-access and has no tenant API key.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableFranceCompanySearchProvider"], out var enableFranceCompanySearch)
            && enableFranceCompanySearch)
        {
            services.AddScoped<CrmFranceCompanySearchProspectingProvider>(_ =>
                new CrmFranceCompanySearchProspectingProvider(
                    new HttpClient(CrmProspectingHttpHandlerFactory.Create("recherche-entreprises.api.gouv.fr"))
                    {
                        Timeout = TimeSpan.FromSeconds(35)
                    }));
            services.AddScoped<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmFranceCompanySearchProspectingProvider>());
        }
        // Serper Google Search: API key is tenant-scoped and sent in X-API-KEY.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableSerperProvider"], out var enableSerper)
            && enableSerper)
        {
            services.AddScoped<CrmSerperSearchProspectingProvider>(provider => new CrmSerperSearchProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("google.serper.dev"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                },
                provider.GetRequiredService<ISaasConfigurationService>()));
            services.AddScoped<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmSerperSearchProspectingProvider>());
        }
        // SerpApi Google Search: its API contract places api_key in the query string.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableSerpApiProvider"], out var enableSerpApi)
            && enableSerpApi)
        {
            services.AddScoped<CrmSerpApiProspectingProvider>(provider => new CrmSerpApiProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("serpapi.com"))
                {
                    Timeout = TimeSpan.FromSeconds(35)
                },
                provider.GetRequiredService<ISaasConfigurationService>()));
            services.AddScoped<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmSerpApiProspectingProvider>());
        }
        // Brave Search uses a tenant subscription token. Search results are unverified web-discovery evidence.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableBraveSearchProvider"], out var enableBraveSearch)
            && enableBraveSearch)
        {
            services.AddScoped<CrmBraveSearchProspectingProvider>(provider => new CrmBraveSearchProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("api.search.brave.com"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                },
                provider.GetRequiredService<ISaasConfigurationService>()));
            services.AddScoped<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmBraveSearchProspectingProvider>());
        }
        // Netherlands KVK is the official Dutch Business Register. It requires a tenant API key
        // and an active subscription; registration evidence is not buyer intent.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableKvkProvider"], out var enableKvk)
            && enableKvk)
        {
            services.AddScoped<CrmKvkProspectingProvider>(provider => new CrmKvkProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("api.kvk.nl"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                },
                provider.GetRequiredService<ISaasConfigurationService>()));
            services.AddScoped<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmKvkProspectingProvider>());
        }
        // Providers resolve API keys from encrypted tenant configuration at execution time.
        // They are Scoped because the prospecting worker establishes the Run tenant before resolving providers.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableUnComtradeProvider"], out var enableUnComtrade)
            && enableUnComtrade)
        {
            services.AddScoped<CrmUnComtradeProspectingProvider>(provider => new CrmUnComtradeProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("comtradeapi.un.org"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                },
                provider.GetRequiredService<ISaasConfigurationService>()));
            services.AddScoped<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmUnComtradeProspectingProvider>());
        }
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableUsCensusTradeProvider"], out var enableUsCensus)
            && enableUsCensus)
        {
            services.AddScoped<CrmUsCensusTradeProspectingProvider>(provider => new CrmUsCensusTradeProspectingProvider(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("api.census.gov"))
                {
                    Timeout = TimeSpan.FromSeconds(30)
                },
                provider.GetRequiredService<ISaasConfigurationService>()));
            services.AddScoped<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmUsCensusTradeProspectingProvider>());
        }
        // GLEIF is an identity-resolution source, not a discovery directory. It is opt-in
        // and only used by the worker to enrich already-discovered candidate names.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableGleifIdentityResolver"], out var enableGleifIdentityResolver)
            && enableGleifIdentityResolver)
        {
            services.AddSingleton<CrmGleifIdentityResolver>(provider => new CrmGleifIdentityResolver(
                new HttpClient(CrmProspectingHttpHandlerFactory.Create("api.gleif.org"))
                {
                    Timeout = TimeSpan.FromSeconds(20)
                }));
            services.AddSingleton<ICrmProspectingIdentityResolver>(provider =>
                provider.GetRequiredService<CrmGleifIdentityResolver>());
        }
        services.AddDataSeeder<CrmPermissionCatalogSeeder>();
        // Menu seeding is intentionally deferred until the corresponding frontend routes/components ship.
        return services;
    }
}
