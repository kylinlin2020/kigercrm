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
        services.AddScoped<ICrmProspectingDomainService, CrmProspectingDomainService>();
        if (bool.TryParse(configuration["XiHan:CRM:Prospecting:EnableFakeProvider"], out var enableFakeProvider)
            && enableFakeProvider)
        {
            services.AddSingleton<ICrmProspectingProvider, FakeCrmProspectingProvider>();
        }
        services.AddDataSeeder<CrmPermissionCatalogSeeder>();
        // Menu seeding is intentionally deferred until the corresponding frontend routes/components ship.
        return services;
    }
}
