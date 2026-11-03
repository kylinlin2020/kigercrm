using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Guards the tenant-scoped quote/order persistence model and its field-security registration.
/// This is source-level contract coverage; database provider execution is still required.
/// </summary>
public sealed class CrmQuoteOrderModelWiringTests
{
    [Theory]
    [InlineData("SysCrmQuote.cs", "Crm_Quote")]
    [InlineData("SysCrmQuoteLine.cs", "Crm_Quote_Line")]
    [InlineData("SysCrmSalesOrder.cs", "Crm_Sales_Order")]
    [InlineData("SysCrmSalesOrderLine.cs", "Crm_Sales_Order_Line")]
    public void CommercialEntities_ShouldBeTenantScopedAndHaveExplicitTableNames(string fileName, string tableName)
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", fileName);
        Assert.Contains("[SugarTable(TableName = \"" + tableName + "\"", source, StringComparison.Ordinal);
        Assert.Contains("BasicAppFullAuditedEntity", source, StringComparison.Ordinal);
        Assert.Contains("IStrictMultiTenantEntity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteAndOrderEntities_ShouldBeRegisteredForFieldSecurityMetadata()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        Assert.Contains(".Add<SysCrmQuote>()", source, StringComparison.Ordinal);
        Assert.Contains(".Add<SysCrmQuoteLine>()", source, StringComparison.Ordinal);
        Assert.Contains(".Add<SysCrmSalesOrder>()", source, StringComparison.Ordinal);
        Assert.Contains(".Add<SysCrmSalesOrderLine>()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommercialLineItems_ShouldUseFixedPrecisionForQuantitiesAndMoney()
    {
        foreach (var fileName in new[] { "SysCrmQuoteLine.cs", "SysCrmSalesOrderLine.cs" })
        {
            var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", fileName);
            Assert.Contains("decimal(18,4)", source, StringComparison.Ordinal);
            Assert.Contains("decimal(9,4)", source, StringComparison.Ordinal);
            Assert.Contains("decimal(18,2)", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void QuoteAndOrderRepositories_ShouldUseTenantAwareSaasRepository()
    {
        foreach (var fileName in new[]
        {
            "CrmQuoteRepository.cs",
            "CrmQuoteLineRepository.cs",
            "CrmSalesOrderRepository.cs",
            "CrmSalesOrderLineRepository.cs"
        })
        {
            var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", fileName);
            Assert.Contains(": SaasRepository<", source, StringComparison.Ordinal);
            Assert.Contains("ISqlSugarClientResolver", source, StringComparison.Ordinal);
        }
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
