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

    [Fact]
    public void SourceQuoteUniqueIndex_ShouldMatchFreshCodeFirstAndUpgradeMigration()
    {
        var entity = File.ReadAllText(Path.GetFullPath(Path.Combine(
            RepositoryRoot(), "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Domain", "Entities", "SysCrmSalesOrder.cs")));
        var sql = File.ReadAllText(Path.GetFullPath(Path.Combine(
            RepositoryRoot(), "backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.4", "5.9.4.sql")));

        Assert.Contains("UX_{table}_TeId_Sq_IsDe", entity, StringComparison.Ordinal);
        var indexStart = entity.IndexOf("UX_{table}_TeId_Sq_IsDe", StringComparison.Ordinal);
        var indexEnd = entity.IndexOf(")]", indexStart, StringComparison.Ordinal);
        Assert.True(indexEnd > indexStart);
        var declaration = entity[indexStart..indexEnd];
        var codeFirstKey = new[] { "TenantId", "SourceQuoteId", "IsDeleted" };
        var previous = -1;
        foreach (var field in codeFirstKey)
        {
            var position = declaration.IndexOf($"nameof({field})", StringComparison.Ordinal);
            Assert.True(position > previous, $"CodeFirst source-quote index must include {field} in migration order.");
            previous = position;
        }

        const string migrationIndex = "ON crm_sales_order (tenant_id, source_quote_id, is_deleted)";
        Assert.Contains(migrationIndex, sql, StringComparison.Ordinal);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
