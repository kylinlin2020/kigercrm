using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmShipmentQueryWiringTests
{
    [Fact]
    public void ShipmentList_ShouldApplyReadPermissionOwnerScopeAndFieldGuard()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmShipmentQueryService.cs");
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", source, StringComparison.Ordinal);
        Assert.Contains("ApplyOwnerScopeAsync", source, StringComparison.Ordinal);
        Assert.Contains("GuardQueryAsync(request.Conditions, typeof(SysCrmShipment)", source, StringComparison.Ordinal);
        Assert.Contains("repository.GetPagedAsync(request, cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentLines_ShouldAuthorizeParentBeforeReturningPagedLines()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmShipmentQueryService.cs");
        Assert.Contains("GetByIdAsync(input.ShipmentId", source, StringComparison.Ordinal);
        Assert.Contains("CanReadOwnedRecord(shipment.OwnerUserId)", source, StringComparison.Ordinal);
        Assert.Contains("x => x.ShipmentId, input.ShipmentId", source, StringComparison.Ordinal);
        Assert.Contains("shipmentLines.GetPagedAsync(request, cancellationToken)", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));
    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
