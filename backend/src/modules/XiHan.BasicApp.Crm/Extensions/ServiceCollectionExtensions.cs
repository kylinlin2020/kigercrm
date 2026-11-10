// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.BasicApp.Crm.Infrastructure.Seeders;
using XiHan.BasicApp.Saas.Extensions;
using XiHan.Framework.Data.Extensions.DependencyInjection;

namespace XiHan.BasicApp.Crm.Extensions;

/// <summary>
/// Composition root for CRM entity metadata, application services, repositories and seeders.
/// Provider-specific registrations live in focused extension classes.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers CRM entities with the platform field-security/entity metadata system.</summary>
    public static IServiceCollection AddCrmFieldSecurityEntities(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
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

    /// <summary>Registers CRM runtime services without mixing provider wiring or seed execution.</summary>
    public static IServiceCollection AddCrmServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddCrmRepositories();
        services.AddCrmDomainServices();
        services.AddCrmExportProviders();
        services.AddCrmProspectingProviders(configuration);
        return services;
    }

    /// <summary>Registers CRM repositories and persistence coordinators.</summary>
    public static IServiceCollection AddCrmRepositories(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ICrmActivityRepository, CrmActivityRepository>();
        services.AddScoped<ICrmContactRepository, CrmContactRepository>();
        services.AddScoped<ICrmCreateRequestRepository, CrmCreateRequestRepository>();
        services.AddScoped<ICrmCustomerRepository, CrmCustomerRepository>();
        services.AddScoped<ICrmLeadRepository, CrmLeadRepository>();
        services.AddScoped<ICrmOpportunityRepository, CrmOpportunityRepository>();
        services.AddScoped<ICrmQuoteLineRepository, CrmQuoteLineRepository>();
        services.AddScoped<ICrmQuoteRepository, CrmQuoteRepository>();
        services.AddScoped<ICrmSalesOrderLineRepository, CrmSalesOrderLineRepository>();
        services.AddScoped<ICrmSalesOrderRepository, CrmSalesOrderRepository>();
        services.AddScoped<ICrmShipmentLineRepository, CrmShipmentLineRepository>();
        services.AddScoped<ICrmShipmentRepository, CrmShipmentRepository>();
        services.AddScoped<ICrmTaskRepository, CrmTaskRepository>();
        services.AddScoped<ICrmCustomerLookupRepository, CrmCustomerLookupRepository>();
        services.AddScoped<ICrmProspectingCampaignRepository, CrmProspectingCampaignRepository>();
        services.AddScoped<ICrmProspectingStrategyRepository, CrmProspectingStrategyRepository>();
        services.AddScoped<ICrmProspectingRunRepository, CrmProspectingRunRepository>();
        services.AddScoped<ICrmProspectingCandidateRepository, CrmProspectingCandidateRepository>();
        services.AddScoped<ICrmProspectingEvidenceRepository, CrmProspectingEvidenceRepository>();
        services.AddScoped<ICrmProspectingScoreRepository, CrmProspectingScoreRepository>();
        services.AddScoped<ICrmProspectingProviderRequestRepository, CrmProspectingProviderRequestRepository>();
        services.AddScoped<ICrmProspectingExtractionJobRepository, CrmProspectingExtractionJobRepository>();
        services.AddScoped<ICrmProspectingRunProviderExecutionRepository, CrmProspectingRunProviderExecutionRepository>();
        services.AddScoped<CrmProspectingCandidatePersistenceService>();
        services.AddScoped<CrmProspectingProviderRegistry>(provider =>
            CrmProspectingProviderRegistry.Create(
                provider.GetServices<ICrmProspectingProvider>(),
                provider.GetServices<ICrmProspectingAsyncExtractionProvider>()));
        return services;
    }

    /// <summary>Registers CRM domain services.</summary>
    public static IServiceCollection AddCrmDomainServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ICrmActivityDomainService, CrmActivityDomainService>();
        services.AddScoped<ICrmContactDomainService, CrmContactDomainService>();
        services.AddScoped<ICrmCustomerDomainService, CrmCustomerDomainService>();
        services.AddScoped<ICrmLeadDomainService, CrmLeadDomainService>();
        services.AddScoped<ICrmOpportunityDomainService, CrmOpportunityDomainService>();
        services.AddScoped<ICrmQuoteDomainService, CrmQuoteDomainService>();
        services.AddScoped<ICrmTaskDomainService, CrmTaskDomainService>();
        services.AddScoped<ICrmProspectingDomainService, CrmProspectingDomainService>();
        return services;
    }

    /// <summary>Registers CRM permission metadata and the CRM menu seeder.</summary>
    /// <remarks>CRM 前端视图（含自动获客）已随模块落地，菜单种子不再推迟；不注册 CrmMenuSeeder 时 sys_menu 里不会有任何 crm_* 行，前端动态菜单因此整组不可见。</remarks>
    public static IServiceCollection AddCrmDataSeeders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddDataSeeder<CrmPermissionCatalogSeeder>();
        services.AddDataSeeder<CrmMenuSeeder>();
        return services;
    }
}
