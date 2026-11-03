using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmShipmentPersistenceWiringTests
{
    [Fact]
    public void ShipmentHeader_ShouldBeTenantScopedAndIndexedByOrder()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmShipment.cs");
        Assert.Contains("IStrictMultiTenantEntity", source, StringComparison.Ordinal);
        Assert.Contains("nameof(TenantId)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(SalesOrderId)", source, StringComparison.Ordinal);
        Assert.Contains("ShipmentNumber", source, StringComparison.Ordinal);
        Assert.Contains("TrackingNumber", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentLines_ShouldPreventDuplicateOrderLineWithinOneShipment()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmShipmentLine.cs");
        Assert.Contains("IStrictMultiTenantEntity", source, StringComparison.Ordinal);
        Assert.Contains("nameof(ShipmentId)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(SalesOrderLineId)", source, StringComparison.Ordinal);
        Assert.Contains("decimal(18,4)", source, StringComparison.Ordinal);
        Assert.Contains("IsDeleted", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesOrderLine_ShouldPersistReservedAndShippedQuantitiesForOptimisticUpdates()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmSalesOrderLine.cs");
        Assert.Contains("Reserved_Quantity", source, StringComparison.Ordinal);
        Assert.Contains("Shipped_Quantity", source, StringComparison.Ordinal);
        Assert.Contains("decimal(18,4)", source, StringComparison.Ordinal);
        Assert.Contains("BasicAppFullAuditedEntity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentEntities_ShouldBeRegisteredForFieldSecurity()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        Assert.Contains(".Add<SysCrmShipment>()", source, StringComparison.Ordinal);
        Assert.Contains(".Add<SysCrmShipmentLine>()", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));
    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
