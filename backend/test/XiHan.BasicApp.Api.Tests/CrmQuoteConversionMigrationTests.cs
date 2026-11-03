namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmQuoteConversionMigrationTests
{
    [Fact]
    public void Migration_ShouldCreateBothTenantScopedUniqueIndexes()
    {
        var sql = File.ReadAllText(Path.GetFullPath(Path.Combine(
            RepositoryRoot(), "backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.4", "5.9.4.sql")));

        Assert.Contains("ux_crm_quote_teid_no_ve_isde", sql, StringComparison.Ordinal);
        Assert.Contains("tenant_id, quote_number, version, is_deleted", sql, StringComparison.Ordinal);
        Assert.Contains("duplicate quote version keys exist", sql, StringComparison.Ordinal);
        Assert.Contains("ux_crm_sales_order_teid_sq_isde", sql, StringComparison.Ordinal);
        Assert.Contains("ux_crm_sales_order_line_teid_or_sql_isde", sql, StringComparison.Ordinal);
        Assert.Contains("tenant_id, source_quote_id, is_deleted", sql, StringComparison.Ordinal);
        Assert.Contains("tenant_id, sales_order_id, source_quote_line_id, is_deleted", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_ShouldFailFastWhenDuplicateActiveSourceLinksExist()
    {
        var sql = File.ReadAllText(Path.GetFullPath(Path.Combine(
            RepositoryRoot(), "backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.4", "5.9.4.sql")));

        Assert.Contains("HAVING COUNT(*) > 1", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY tenant_id, source_quote_id, is_deleted", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY tenant_id, sales_order_id, source_quote_line_id, is_deleted", sql, StringComparison.Ordinal);
        Assert.Contains("including soft-deleted rows", sql, StringComparison.Ordinal);
        Assert.Contains("Resolve duplicates, then retry.", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS", sql, StringComparison.Ordinal);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
