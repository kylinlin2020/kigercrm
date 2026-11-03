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

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
