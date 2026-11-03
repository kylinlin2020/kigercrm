namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmShipmentFulfillmentMigrationTests
{
    [Fact]
    public void Migration_ShouldAddOrderRevisionAndQuantityLedgerWithSafeDefaults()
    {
        var sql = File.ReadAllText(Path.GetFullPath(Path.Combine(
            RepositoryRoot(), "backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.5", "5.9.5.sql")));

        Assert.Contains("ADD COLUMN IF NOT EXISTS fulfillment_revision integer NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS reserved_quantity numeric(18,4) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS shipped_quantity numeric(18,4) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_ShouldInstallNonNegativeAndNoOverShipmentConstraintIdempotently()
    {
        var sql = File.ReadAllText(Path.GetFullPath(Path.Combine(
            RepositoryRoot(), "backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.5", "5.9.5.sql")));

        Assert.Contains("IF NOT EXISTS (", sql, StringComparison.Ordinal);
        Assert.Contains("ck_crm_sales_order_line_fulfillment_qty", sql, StringComparison.Ordinal);
        Assert.Contains("reserved_quantity >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("shipped_quantity >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("reserved_quantity + shipped_quantity <= quantity", sql, StringComparison.Ordinal);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
