// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using XiHan.BasicApp.Crm.Domain.Entities;
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
            .Add<SysCrmContact>());
    }
}
