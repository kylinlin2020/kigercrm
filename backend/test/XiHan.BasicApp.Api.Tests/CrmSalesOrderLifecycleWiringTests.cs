using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level guards for authorized sales order state transitions.</summary>
public sealed class CrmSalesOrderLifecycleWiringTests
{
    [Fact]
    public void StatusCommand_ShouldRequireExecutePermissionAndTransactionalOwnerScopedUpdate()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        Assert.Contains("[UnitOfWork(true)]", source, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync", source, StringComparison.Ordinal);
        Assert.Contains("EnsureUpdatableAsync(typeof(SysCrmSalesOrder)", source, StringComparison.Ordinal);
        Assert.Contains("repository.UpdateAsync(order, cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusCommand_ShouldPreventIllegalAndTerminalTransitions()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        Assert.Contains("CrmSalesOrderStatus.Draft => target is CrmSalesOrderStatus.Confirmed or CrmSalesOrderStatus.Cancelled", source, StringComparison.Ordinal);
        Assert.Contains("CrmSalesOrderStatus.Processing => false", source, StringComparison.Ordinal);
        Assert.Contains("CrmSalesOrderStatus.PartiallyShipped => false", source, StringComparison.Ordinal);
        Assert.Contains("Fulfillment-derived states are written only by shipment workflows", source, StringComparison.Ordinal);
        Assert.Contains("_ => false", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
