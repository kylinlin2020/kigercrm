// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.Crm.Extensions;
using XiHan.BasicApp.Saas;
using XiHan.Framework.Core.Modularity;

namespace XiHan.BasicApp.Crm;

/// <summary>
/// KIGER CRM business module. Platform identity, tenancy and authorization remain owned by BasicApp SaaS.
/// </summary>
[DependsOn(typeof(XiHanBasicAppSaasModule))]
public sealed class XiHanBasicAppCrmModule : XiHanModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddCrmFieldSecurityEntities();
        context.Services.AddCrmDataSeeders();
    }
}
