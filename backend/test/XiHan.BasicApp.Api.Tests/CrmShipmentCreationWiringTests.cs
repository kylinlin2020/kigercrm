using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmShipmentCreationWiringTests
{
    [Fact]
    public void ShipmentCreation_ShouldBeTransactionalAndRequireCreatePermission()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        Assert.Contains("[UnitOfWork(true)]", source, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]", source, StringComparison.Ordinal);
        Assert.Contains("shipments.AddAsync", source, StringComparison.Ordinal);
        Assert.Contains("shipmentLines.AddAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCreation_ShouldValidateOrderScopeLineOwnershipAndQuantity()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        Assert.Contains("EnsureRecordAccessibleAsync", source, StringComparison.Ordinal);
        Assert.Contains("orderLine.SalesOrderId != order.BasicId", source, StringComparison.Ordinal);
        Assert.Contains("requested.Quantity > orderLine.Quantity", source, StringComparison.Ordinal);
        Assert.Contains("Distinct().Count()", source, StringComparison.Ordinal);
        Assert.Contains("CrmSalesOrderStatus.Confirmed", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));
    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
