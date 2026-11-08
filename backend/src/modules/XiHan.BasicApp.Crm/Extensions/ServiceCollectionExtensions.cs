// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Application.Exporting;
using XiHan.BasicApp.Saas.Application.Exporting;
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
            .Add<SysCrmProspectingProviderCoverage>()
            .Add<SysCrmProspectingExtractionJob>());
        
    }

    /// <summary>Registers CRM services and seeders; synthetic prospecting is opt-in for local smoke tests only.</summary>
    public static IServiceCollection AddCrmDataSeeders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        // CRM export providers reuse the existing permission-checked, tenant-scoped query services.
        services.AddScoped<IExportProvider, CrmLeadExportProvider>();
        services.AddScoped<IExportProvider, CrmCustomerExportProvider>();
        services.AddScoped<IExportProvider, CrmContactExportProvider>();
        services.AddScoped<IExportProvider, CrmOpportunityExportProvider>();
        services.AddScoped<IExportProvider, CrmQuoteExportProvider>();
        services.AddScoped<IExportProvider, CrmSalesOrderExportProvider>();
        services.AddScoped<IExportProvider, CrmShipmentExportProvider>();
        services.AddScoped<ICrmProspectingCampaignRepository, CrmProspectingCampaignRepository>();
        services.AddScoped<ICrmProspectingStrategyRepository, CrmProspectingStrategyRepository>();
        services.AddScoped<ICrmProspectingRunRepository, CrmProspectingRunRepository>();
        services.AddScoped<ICrmProspectingCandidateRepository, CrmProspectingCandidateRepository>();
        services.AddScoped<ICrmProspectingEvidenceRepository, CrmProspectingEvidenceRepository>();
        services.AddScoped<ICrmProspectingScoreRepository, CrmProspectingScoreRepository>();
        services.AddScoped<ICrmProspectingProviderRequestRepository, CrmProspectingProviderRequestRepository>();
        services.AddScoped<ICrmProspectingExtractionJobRepository, CrmProspectingExtractionJobRepository>();
        // Resolve provider capabilities from the same scoped registrations used by the worker.
        // The registry validates normalized codes across sync/async contracts at resolution time.
        services.AddScoped<CrmProspectingProviderRegistry>(provider =>
            CrmProspectingProviderRegistry.Create(
                provider.GetServices<ICrmProspectingProvider>(),
                provider.GetServices<ICrmProspectingAsyncExtractionProvider>()));
        services.AddScoped<ICrmProspectingRunProviderExecutionRepository, CrmProspectingRunProviderExecutionRepository>();
        services.AddScoped<CrmProspectingCandidatePersistenceService>();
        // CrmProspectingRunHostedService follows the framework worker convention:
        // XiHanBackgroundServiceBase supplies IBackgroundWorker/SingletonDependency and the HostedService suffix
        // is auto-exposed as IHostedService. Do not call AddHostedService here (it would double-consume the queue).
        services.AddScoped<ICrmProspectingDomainService, CrmProspectingDomainService>();
        services.AddCrmProspectingProviders(configuration);

        services.AddDataSeeder<CrmPermissionCatalogSeeder>();
        // Menu seeding is intentionally deferred until the corresponding frontend routes/components ship.
        return services;
    }
}
