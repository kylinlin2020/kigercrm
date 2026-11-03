using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmPermanentDocumentNumberUniquenessTests
{
    [Fact]
    public void QuoteOrderAndShipmentIndexes_ShouldExcludeSoftDeleteFromUniqueKeys()
    {
        var quote = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmQuote.cs");
        var order = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmSalesOrder.cs");
        var shipment = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmShipment.cs");

        Assert.Contains("UX_{table}_TeId_No_Ve", quote, StringComparison.Ordinal);
        Assert.Contains("nameof(TenantId)", quote, StringComparison.Ordinal);
        Assert.Contains("nameof(QuoteNumber)", quote, StringComparison.Ordinal);
        Assert.Contains("nameof(Version)", quote, StringComparison.Ordinal);
        Assert.Contains("UX_{table}_TeId_No", order, StringComparison.Ordinal);
        Assert.Contains("nameof(OrderNumber)", order, StringComparison.Ordinal);
        Assert.Contains("UX_{table}_TeId_No", shipment, StringComparison.Ordinal);
        Assert.Contains("nameof(ShipmentNumber)", shipment, StringComparison.Ordinal);

        Assert.DoesNotContain("UX_{table}_TeId_No_Ve_IsDe", quote, StringComparison.Ordinal);
        Assert.DoesNotContain("UX_{table}_TeId_No_IsDe", order, StringComparison.Ordinal);
        Assert.DoesNotContain("UX_{table}_TeId_No_IsDe", shipment, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_ShouldPreflightHistoricalDuplicatesBeforeReplacingIndexes()
    {
        var sql = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.7", "5.9.7.sql");

        var quoteCheck = sql.IndexOf("GROUP BY tenant_id, quote_number, version", StringComparison.Ordinal);
        var orderCheck = sql.IndexOf("GROUP BY tenant_id, order_number", StringComparison.Ordinal);
        var shipmentCheck = sql.IndexOf("GROUP BY tenant_id, shipment_number", StringComparison.Ordinal);
        var dropOldIndexes = sql.IndexOf("DROP INDEX IF EXISTS", StringComparison.Ordinal);
        var createPermanentIndexes = sql.IndexOf("CREATE UNIQUE INDEX IF NOT EXISTS", StringComparison.Ordinal);

        Assert.True(quoteCheck >= 0 && dropOldIndexes > quoteCheck);
        Assert.True(orderCheck >= 0 && dropOldIndexes > orderCheck);
        Assert.True(shipmentCheck >= 0 && dropOldIndexes > shipmentCheck);
        Assert.True(createPermanentIndexes > dropOldIndexes);
        Assert.Contains("crm_quote (tenant_id, quote_number, version)", sql, StringComparison.Ordinal);
        Assert.Contains("crm_sales_order (tenant_id, order_number)", sql, StringComparison.Ordinal);
        Assert.Contains("crm_shipment (tenant_id, shipment_number)", sql, StringComparison.Ordinal);
        Assert.Contains("soft-deleted rows", sql, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
