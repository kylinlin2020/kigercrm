namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmSalesOrderQuoteLineUniquenessTests
{
    [Fact]
    public void SalesOrderLines_ShouldPreventCopyingSameQuoteLineTwiceWithinOneOrder()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            RepositoryRoot(), "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Domain", "Entities", "SysCrmSalesOrderLine.cs")));

        Assert.Contains("UX_{table}_TeId_Or_SqLi_IsDe", source, StringComparison.Ordinal);
        Assert.Contains("nameof(TenantId)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(SalesOrderId)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(SourceQuoteLineId)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(IsDeleted)", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
