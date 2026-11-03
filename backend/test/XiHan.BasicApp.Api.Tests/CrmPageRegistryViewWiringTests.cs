using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Keeps the backend CRM PageRegistry component keys aligned with the implemented frontend module views.
/// </summary>
public sealed class CrmPageRegistryViewWiringTests
{
    [Theory]
    [InlineData("Crm/Customer/Index", "customer")]
    [InlineData("Crm/Contact/Index", "contact")]
    [InlineData("Crm/Activity/Index", "activity")]
    [InlineData("Crm/Lead/Index", "lead")]
    [InlineData("Crm/Opportunity/Index", "opportunity")]
    [InlineData("Crm/Task/Index", "task")]
    [InlineData("Crm/Quote/Index", "quote")]
    [InlineData("Crm/SalesOrder/Index", "sales-order")]
    [InlineData("Crm/Shipment/Index", "shipment")]
    public void RegisteredCrmComponent_ShouldResolveToAnExistingModuleView(string component, string view)
    {
        var registry = File.ReadAllText(RepositoryPath(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Pages", "PageRegistry.cs"));
        Assert.Contains(component, registry, StringComparison.Ordinal);

        var viewPath = RepositoryPath(
            "frontend", "src", "modules", "crm", "views", "crm", view, "index.vue");
        Assert.True(File.Exists(viewPath), $"CRM component {component} has no frontend view at {viewPath}.");
    }

    [Fact]
    public void CrmMenuSeeder_ShouldConsumeTheSinglePageRegistry()
    {
        var source = File.ReadAllText(RepositoryPath(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Seeders", "CrmMenuSeeder.cs"));

        Assert.Contains("protected override IReadOnlyList<PageDescriptor> Pages => PageRegistry.All;", source, StringComparison.Ordinal);
        Assert.Contains("protected override IReadOnlyList<ButtonDescriptor> Buttons => PageRegistry.Buttons;", source, StringComparison.Ordinal);
        var registry = File.ReadAllText(RepositoryPath("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Pages", "PageRegistry.cs"));
        Assert.Contains("new(\"crm_sales_orders.create\", \"Create\", \"crm_sales_orders\", CrmPermissionCodes.Create", registry, StringComparison.Ordinal);
        Assert.Contains("new(\"crm_sales_orders.update\", \"Edit\", \"crm_sales_orders\", CrmPermissionCodes.Update", registry, StringComparison.Ordinal);
    }

    private static string RepositoryPath(params string[] segments) =>
        Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments)));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
