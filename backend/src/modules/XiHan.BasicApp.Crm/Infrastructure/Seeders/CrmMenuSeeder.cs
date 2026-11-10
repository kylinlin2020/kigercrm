using Microsoft.Extensions.Logging;
using XiHan.BasicApp.Saas.Application.Pages;
using XiHan.BasicApp.Saas.Infrastructure.Seeders;
using XiHan.Framework.Data.SqlSugar.Clients;
using PageRegistry = XiHan.BasicApp.Crm.Application.Pages.PageRegistry;

namespace XiHan.BasicApp.Crm.Infrastructure.Seeders;

/// <summary>Seeds CRM pages and button metadata from PageRegistry.</summary>
public sealed class CrmMenuSeeder(
    ISqlSugarClientResolver clientResolver,
    ILogger<CrmMenuSeeder> logger,
    IServiceProvider serviceProvider)
    : PageRegistryMenuSeederBase(clientResolver, logger, serviceProvider)
{
    public override int Order => SeedOrders.Menus + 60;
    public override string Name => "[CRM] Menu";
    protected override IReadOnlyList<PageDescriptor> Pages => PageRegistry.All;
    protected override IReadOnlyList<ButtonDescriptor> Buttons => PageRegistry.Buttons;
}
