// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Configuration;
using System.Net;
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
            .Add<SysCrmProspectingScore>());
        
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
                new HttpClient(new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                })
                {
                    Timeout = TimeSpan.FromSeconds(25)
                }));
            services.AddSingleton<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmUsaSpendingProspectingProvider>());
        }
        // Official UK procurement notices are public OCDS data. The adapter is opt-in and
        // uses a fixed endpoint with redirects disabled.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableFindTenderProvider"], out var enableFindTenderProvider)
            && enableFindTenderProvider)
        {
            services.AddSingleton<CrmFindTenderProspectingProvider>(provider => new CrmFindTenderProspectingProvider(
                new HttpClient(new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                })
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
                new HttpClient(new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                })
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
                new HttpClient(new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                })
                {
                    Timeout = TimeSpan.FromSeconds(30)
                }));
            services.AddSingleton<ICrmProspectingProvider>(provider =>
                provider.GetRequiredService<CrmTedProspectingProvider>());
        }
        // GLEIF is an identity-resolution source, not a discovery directory. It is opt-in
        // and only used by the worker to enrich already-discovered candidate names.
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableGleifIdentityResolver"], out var enableGleifIdentityResolver)
            && enableGleifIdentityResolver)
        {
            services.AddSingleton<CrmGleifIdentityResolver>(provider => new CrmGleifIdentityResolver(
                new HttpClient(new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                })
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
