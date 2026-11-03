using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level guards for quote creation transaction, tenant relationships and server-side pricing.</summary>
public sealed class CrmQuoteCreationWiringTests
{
    [Fact]
    public void QuoteCreation_ShouldRunInTransactionalAuthorizedApplicationService()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        Assert.Contains("[UnitOfWork(true)]", source, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]", source, StringComparison.Ordinal);
        Assert.Contains("EnsureAssignableAsync", source, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmQuote)", source, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmQuoteLine)", source, StringComparison.Ordinal);
        Assert.Contains("domainService.CreateAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteCreation_ShouldValidateCustomerContactAndOpportunityOwnership()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "DomainServices", "Implementations", "CrmQuoteDomainService.cs");
        Assert.Contains("Customer does not exist in the current tenant.", source, StringComparison.Ordinal);
        Assert.Contains("Contact does not exist in the current tenant.", source, StringComparison.Ordinal);
        Assert.Contains("contact.CustomerId != customer.BasicId", source, StringComparison.Ordinal);
        Assert.Contains("Opportunity does not exist in the current tenant.", source, StringComparison.Ordinal);
        Assert.Contains("opportunity.CustomerId != customer.BasicId", source, StringComparison.Ordinal);
        Assert.Contains("At least one quotation line is required.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteLineAmounts_ShouldBeCalculatedOnServerUsingPersistedPrecision()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "DomainServices", "Implementations", "CrmQuoteDomainService.cs");
        Assert.Contains("decimal.Round(input.Quantity, 4, MidpointRounding.AwayFromZero)", source, StringComparison.Ordinal);
        Assert.Contains("decimal.Round(input.UnitPrice, 4, MidpointRounding.AwayFromZero)", source, StringComparison.Ordinal);
        Assert.Contains("Money(quantity * unitPrice)", source, StringComparison.Ordinal);
        Assert.Contains("Money(subtotal * discountRate / 100m)", source, StringComparison.Ordinal);
        Assert.Contains("Money(taxable * taxRate / 100m)", source, StringComparison.Ordinal);
        Assert.Contains("MidpointRounding.AwayFromZero", source, StringComparison.Ordinal);
        Assert.Contains("GrandTotal = total", source, StringComparison.Ordinal);
        Assert.Contains("LineTotal = taxable + tax", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteCreation_ShouldPersistHeaderAndLinesInsideOneUnitOfWork()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "DomainServices", "Implementations", "CrmQuoteDomainService.cs");
        var headerInsert = source.IndexOf("await quotes.AddAsync", StringComparison.Ordinal);
        var lineInsert = source.IndexOf("await quoteLines.AddAsync", StringComparison.Ordinal);
        Assert.True(headerInsert >= 0 && lineInsert > headerInsert);
        var appService = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        Assert.Contains("[UnitOfWork(true)]", appService, StringComparison.Ordinal);
    }


    [Fact]
    public void QuoteCreation_ShouldSupportOptionalIdempotentServerNumbering()
    {
        var dto = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmQuoteCommandDtos.cs");
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        Assert.Contains("NumberingIdempotencyKey", dto, StringComparison.Ordinal);
        Assert.Contains("INumberGenerator numberGenerator", service, StringComparison.Ordinal);
        Assert.Contains("CrmNumberingRuleCodes.Quote", service, StringComparison.Ordinal);
        Assert.Contains("NumberingScope.Auto", service, StringComparison.Ordinal);
        Assert.Contains('"CRM_QUOTE"', service, StringComparison.Ordinal);
        Assert.Contains("command = command with { QuoteNumber = allocation.Numbers.Single() }", service, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
