using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level guards for direct sales order creation.</summary>
public sealed class CrmSalesOrderCreateWiringTests
{
    [Fact]
    public void CreateCommand_ShouldBeTransactionalAndRequireCreatePermission()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var method = source[source.IndexOf("CreateAsync(CrmSalesOrderCreateDto", StringComparison.Ordinal)..source.IndexOf("private static SysCrmSalesOrderLine CalculateLine", StringComparison.Ordinal)];
        Assert.Contains("[UnitOfWork(true)]", source, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]", method, StringComparison.Ordinal);
        Assert.Contains("repository.AddAsync(order", method, StringComparison.Ordinal);
        Assert.Contains("orderLines.AddAsync(line", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCommand_ShouldValidateTenantRelatedRecordsAndTheirOwnerScopes()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var method = source[source.IndexOf("CreateAsync(CrmSalesOrderCreateDto", StringComparison.Ordinal)..source.IndexOf("private static SysCrmSalesOrderLine CalculateLine", StringComparison.Ordinal)];
        Assert.Contains("customers.GetByIdAsync", method, StringComparison.Ordinal);
        Assert.Contains("contacts.GetByIdAsync", method, StringComparison.Ordinal);
        Assert.Contains("opportunities.GetByIdAsync", method, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, customer.OwnerUserId", method, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, contact.OwnerUserId", method, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, opportunity.OwnerUserId", method, StringComparison.Ordinal);
        Assert.Contains("contact.CustomerId != customer.BasicId", method, StringComparison.Ordinal);
        Assert.Contains("opportunity.CustomerId != customer.BasicId", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCommand_ShouldCalculateAmountsOnServerAndProtectCreatedFields()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var method = source[source.IndexOf("CreateAsync(CrmSalesOrderCreateDto", StringComparison.Ordinal)..source.IndexOf("private static SysCrmSalesOrderLine CalculateLine", StringComparison.Ordinal)];
        Assert.Contains("calculated.Sum(x => x.LineSubtotal)", method, StringComparison.Ordinal);
        Assert.Contains("calculated.Sum(x => x.LineDiscount)", method, StringComparison.Ordinal);
        Assert.Contains("calculated.Sum(x => x.LineTax)", method, StringComparison.Ordinal);
        Assert.Contains("calculated.Sum(x => x.LineTotal)", method, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmSalesOrder)", method, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmSalesOrderLine)", source, StringComparison.Ordinal);
        Assert.Contains("CrmSalesOrderStatus.Draft", method, StringComparison.Ordinal);
        Assert.Contains("Lines.Count > 500", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentCreate_ShouldReserveOnlyRemainingOrderQuantityUnderOrderLock()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var method = source[source.IndexOf("CreateAsync(CrmShipmentCreateDto", StringComparison.Ordinal)..source.IndexOf("ChangeStatusAsync(CrmShipmentStatusUpdateDto", StringComparison.Ordinal)];
        Assert.Contains("order.FulfillmentRevision++", method, StringComparison.Ordinal);
        Assert.Contains("orderLine.Quantity - orderLine.ReservedQuantity - orderLine.ShippedQuantity", method, StringComparison.Ordinal);
        Assert.Contains("requested.Quantity > availableQuantity", method, StringComparison.Ordinal);
        Assert.Contains("orderLine.ReservedQuantity += requested.Quantity", method, StringComparison.Ordinal);
        Assert.Contains("OrderBy(line => line.SalesOrderLineId)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentLifecycle_ShouldReleaseReservationsOnCancelAndConsumeThemOnDispatch()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmShipmentAppService.cs");
        var cancel = source[source.IndexOf("if (target == CrmShipmentStatus.Cancelled)", StringComparison.Ordinal)..source.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", StringComparison.Ordinal)];
        var dispatch = source[source.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", StringComparison.Ordinal)..source.IndexOf("else\n        {", source.IndexOf("else if (target == CrmShipmentStatus.Dispatched)", StringComparison.Ordinal), StringComparison.Ordinal)];
        Assert.Contains("orderLine.ReservedQuantity -= shipmentLine.Quantity", cancel, StringComparison.Ordinal);
        Assert.Contains("orderLine.ReservedQuantity -= shipmentLine.Quantity", dispatch, StringComparison.Ordinal);
        Assert.Contains("orderLine.ShippedQuantity += shipmentLine.Quantity", dispatch, StringComparison.Ordinal);
        Assert.Contains("CrmSalesOrderStatus.Completed", dispatch, StringComparison.Ordinal);
        Assert.Contains("CrmSalesOrderStatus.PartiallyShipped", dispatch, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
