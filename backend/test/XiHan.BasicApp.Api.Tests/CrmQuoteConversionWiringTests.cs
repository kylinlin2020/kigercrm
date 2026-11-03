using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmQuoteConversionWiringTests
{
    [Fact]
    public void ConversionEndpoint_ShouldBeExecuteAuthorizedTransactionalAndOwnerScoped()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        var method = app[app.IndexOf("ConvertToSalesOrderAsync", StringComparison.Ordinal)..];
        Assert.Contains("[UnitOfWork(true)]", method, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", method, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, quote.OwnerUserId", method, StringComparison.Ordinal);
        Assert.Contains("CrmQuoteStatus.Accepted", method, StringComparison.Ordinal);
        Assert.Contains("quote.ConvertedOrderId.HasValue", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversionEndpoint_ShouldCopyQuoteHeaderAndLinesAndLinkConvertedOrder()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        var method = app[app.IndexOf("ConvertToSalesOrderAsync", StringComparison.Ordinal)..];
        Assert.Contains("SourceQuoteId = quote.BasicId", method, StringComparison.Ordinal);
        Assert.Contains("CustomerId = quote.CustomerId", method, StringComparison.Ordinal);
        Assert.Contains("GrandTotal = quote.GrandTotal", method, StringComparison.Ordinal);
        Assert.Contains("SourceQuoteLineId = sourceLine.BasicId", method, StringComparison.Ordinal);
        Assert.Contains("Quantity = sourceLine.Quantity", method, StringComparison.Ordinal);
        Assert.Contains("await salesOrderLines.AddAsync(orderLine", method, StringComparison.Ordinal);
        Assert.Contains("quote.Status = CrmQuoteStatus.Converted", method, StringComparison.Ordinal);
        Assert.Contains("quote.ConvertedOrderId = createdOrder.BasicId", method, StringComparison.Ordinal);
        Assert.Contains("await records.UpdateAsync(quote", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversionEndpoint_ShouldApplyFieldSecurityToCreatedRecords()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        var method = app[app.IndexOf("ConvertToSalesOrderAsync", StringComparison.Ordinal)..];
        Assert.Contains("EnsureUpdatableAsync(typeof(SysCrmQuote)", method, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmSalesOrder)", method, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmSalesOrderLine)", method, StringComparison.Ordinal);
        Assert.Contains("ListForQuoteAsync", method, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
