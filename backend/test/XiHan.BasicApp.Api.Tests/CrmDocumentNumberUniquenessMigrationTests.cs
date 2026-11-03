using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmDocumentNumberUniquenessMigrationTests
{
    [Fact]
    public void Migration_ShouldCreateTenantScopedSalesOrderAndShipmentNumberIndexes()
    {
        var sql = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.6", "5.9.6.sql");

        Assert.Contains("ux_crm_sales_order_teid_no_isde", sql, StringComparison.Ordinal);
        Assert.Contains("ON crm_sales_order (tenant_id, order_number, is_deleted)", sql, StringComparison.Ordinal);
        Assert.Contains("ux_crm_shipment_teid_no_isde", sql, StringComparison.Ordinal);
        Assert.Contains("ON crm_shipment (tenant_id, shipment_number, is_deleted)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_ShouldBlockDuplicateLegacyKeysBeforeCreatingIndexes()
    {
        var sql = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.6", "5.9.6.sql");

        var orderCheck = sql.IndexOf("GROUP BY tenant_id, order_number, is_deleted", StringComparison.Ordinal);
        var orderIndex = sql.IndexOf("CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_sales_order_teid_no_isde", StringComparison.Ordinal);
        var shipmentCheck = sql.IndexOf("GROUP BY tenant_id, shipment_number, is_deleted", StringComparison.Ordinal);
        var shipmentIndex = sql.IndexOf("CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_shipment_teid_no_isde", StringComparison.Ordinal);

        Assert.True(orderCheck >= 0 && orderIndex > orderCheck);
        Assert.True(shipmentCheck >= 0 && shipmentIndex > shipmentCheck);
        Assert.Contains("Resolve duplicates, then retry.", sql, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS", sql, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
