using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmSalesOrderDetailQueryWiringTests
{
    [Fact]
    public void SalesOrderDetail_ShouldEnforceParentOwnerScopeAndExposeTerms()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmSalesOrderQueryService.cs");
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", source, StringComparison.Ordinal);
        Assert.Contains("ResolveAccessibleUsersAsync", source, StringComparison.Ordinal);
        Assert.Contains("CanReadOwnedRecord(order.OwnerUserId)", source, StringComparison.Ordinal);
        Assert.Contains("CustomerPurchaseOrderNumber = order.CustomerPurchaseOrderNumber", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesOrderDetail_ShouldExposePersistedTermsAndConditionsSnapshot()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmSalesOrderQueryService.cs");
        var dto = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmSalesOrderQueryDtos.cs");

        Assert.Contains("public string? TermsAndConditions { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("TermsAndConditions = order.TermsAndConditions", source, StringComparison.Ordinal);
        Assert.Contains("CustomerPurchaseOrderNumber = order.CustomerPurchaseOrderNumber", source, StringComparison.Ordinal);
        Assert.Contains("PaymentTerms = order.PaymentTerms", source, StringComparison.Ordinal);
        Assert.Contains("DeliveryTerms = order.DeliveryTerms", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesOrderLines_ShouldBePagedSortedAndFieldGuardedAfterParentAuthorization()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmSalesOrderQueryService.cs");
        Assert.Contains("x => x.SalesOrderId, input.SalesOrderId", source, StringComparison.Ordinal);
        Assert.Contains("GuardQueryAsync(request.Conditions, typeof(SysCrmSalesOrderLine)", source, StringComparison.Ordinal);
        Assert.Contains("orderLines.GetPagedAsync(request, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("SortOrder", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));
    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
