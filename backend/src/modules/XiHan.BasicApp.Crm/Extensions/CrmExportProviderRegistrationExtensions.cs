// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using XiHan.BasicApp.Crm.Application.Exporting;
using XiHan.BasicApp.Saas.Application.Exporting;

namespace XiHan.BasicApp.Crm.Extensions;

/// <summary>Registers CRM query projections on the shared asynchronous export pipeline.</summary>
public static class CrmExportProviderRegistrationExtensions
{
    public static IServiceCollection AddCrmExportProviders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IExportProvider, CrmLeadExportProvider>();
        services.AddScoped<IExportProvider, CrmCustomerExportProvider>();
        services.AddScoped<IExportProvider, CrmContactExportProvider>();
        services.AddScoped<IExportProvider, CrmOpportunityExportProvider>();
        services.AddScoped<IExportProvider, CrmQuoteExportProvider>();
        services.AddScoped<IExportProvider, CrmSalesOrderExportProvider>();
        services.AddScoped<IExportProvider, CrmShipmentExportProvider>();
        return services;
    }
}
