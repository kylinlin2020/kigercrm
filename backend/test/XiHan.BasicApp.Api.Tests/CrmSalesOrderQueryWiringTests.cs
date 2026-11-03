using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level guards for sales order query permissions and scoping.</summary>
public sealed class CrmSalesOrderQueryWiringTests
{
    [Fact]
    public void SalesOrderPageQuery_ShouldEnforceReadOwnerAndFieldScopes()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmSalesOrderQueryService.cs");
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", source, StringComparison.Ordinal);
        Assert.Contains("ApplyOwnerScopeAsync", source, StringComparison.Ordinal);
        Assert.Contains("GuardQueryAsync(request.Conditions, typeof(SysCrmSalesOrder)", source, StringComparison.Ordinal);
        Assert.Contains("repository.GetPagedAsync(request, cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesOrderPageQuery_ShouldValidateStatusAndReturnOrderSummary()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmSalesOrderQueryService.cs");
        Assert.Contains("Enum.IsDefined(typeof(CrmSalesOrderStatus), input.Status.Value)", source, StringComparison.Ordinal);
        Assert.Contains("x.OrderNumber, x => x.CustomerNameSnapshot", source, StringComparison.Ordinal);
        var dto = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmSalesOrderQueryDtos.cs");
        Assert.Contains("GrandTotal", dto, StringComparison.Ordinal);
        Assert.Contains("SourceQuoteId", dto, StringComparison.Ordinal);
        Assert.Contains("OrderDate", dto, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
