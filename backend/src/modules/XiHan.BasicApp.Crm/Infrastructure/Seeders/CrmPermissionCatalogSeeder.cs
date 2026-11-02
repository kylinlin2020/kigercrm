using Microsoft.Extensions.Logging;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Domain.Entities;
using XiHan.BasicApp.Saas.Infrastructure.Seeders;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Seeders;

/// <summary>Seeds tenant-side CRM resource permissions.</summary>
public sealed class CrmPermissionCatalogSeeder(
    ISqlSugarClientResolver clientResolver,
    ILogger<CrmPermissionCatalogSeeder> logger,
    IServiceProvider serviceProvider)
    : PermissionCatalogSeederBase(clientResolver, logger, serviceProvider)
{
    private static readonly ResourceSeed Crm = new(CrmPermissionCodes.Resource, "CRM", "/api/crm", "Customers, contacts, leads, opportunities and tasks", 500);

    public override int Order => SeedOrders.PermissionCatalog + 40;
    public override string Name => "[CRM] Permission catalog";
    public override string ModuleCode => CrmPermissionCodes.Module;
    public override IReadOnlyList<ResourceSeed> Resources { get; } = [Crm];

    public override IReadOnlyList<PermissionSeed> Permissions { get; } =
    [
        .. PermissionSeed.Of(Crm, PermissionSide.Tenant, 5000,
            OperationSeeds.Read, OperationSeeds.Create, OperationSeeds.Update, OperationSeeds.Delete, OperationSeeds.Execute),
    ];
}
