using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmSalesOrderSourceQuoteUniquenessTests
{
    [Fact]
    public void SalesOrder_ShouldHaveTenantScopedUniqueSourceQuoteConstraint()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmSalesOrder.cs");
        Assert.Contains("UX_{table}_TeId_Sq_IsDe", source, StringComparison.Ordinal);
        Assert.Contains("nameof(TenantId)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(SourceQuoteId)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(IsDeleted)", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));
    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
