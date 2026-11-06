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
    public void ConversionEndpoint_ShouldReturnPreviouslyCreatedOrderWhenARequestIsRetried()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        var method = app[app.IndexOf("ConvertToSalesOrderAsync", StringComparison.Ordinal)..];
        Assert.Contains("quote.ConvertedOrderId.HasValue", method, StringComparison.Ordinal);
        Assert.Contains("salesOrders.GetByIdAsync(quote.ConvertedOrderId.Value", method, StringComparison.Ordinal);
        Assert.Contains("existingOrder.SourceQuoteId != quote.BasicId", method, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, existingOrder.OwnerUserId", method, StringComparison.Ordinal);
        Assert.Contains("return new CrmSalesOrderListItemDto", method, StringComparison.Ordinal);
        Assert.Contains("The quote is marked converted, but its linked sales order does not exist.", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversionEndpoint_ShouldFailClosedWhenQuoteStatusAndOrderLinkDisagree()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        var methodStart = app.IndexOf("public async Task<CrmSalesOrderListItemDto> ConvertToSalesOrderAsync", StringComparison.Ordinal);
        var nextMethod = app.IndexOf("private async Task EnsureRelatedRecordsAccessibleAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && nextMethod > methodStart);
        var method = app[methodStart..nextMethod];

        var convertedWithoutLink = method.IndexOf("quote.Status == CrmQuoteStatus.Converted && !quote.ConvertedOrderId.HasValue", StringComparison.Ordinal);
        var acceptedWithLink = method.IndexOf("quote.Status == CrmQuoteStatus.Accepted && quote.ConvertedOrderId.HasValue", StringComparison.Ordinal);
        var linkedOrderReplay = method.IndexOf("if (quote.ConvertedOrderId.HasValue)", StringComparison.Ordinal);
        var orderCreation = method.IndexOf("salesOrders.AddAsync(order, cancellationToken)", StringComparison.Ordinal);

        Assert.True(convertedWithoutLink >= 0 && acceptedWithLink > convertedWithoutLink);
        Assert.True(linkedOrderReplay > acceptedWithLink);
        Assert.True(orderCreation > linkedOrderReplay);
        Assert.Contains("converted quote has no linked sales order", method, StringComparison.Ordinal);
        Assert.Contains("accepted quote already has a converted-order link", method, StringComparison.Ordinal);
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

    [Fact]
    public void ConversionEndpoint_ShouldUseSourceQuoteUniqueGuardAndCommitQuoteLinkWithOrderSnapshot()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmSalesOrder.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.4", "5.9.4.sql");

        var methodStart = app.IndexOf("public async Task<CrmSalesOrderListItemDto> ConvertToSalesOrderAsync", StringComparison.Ordinal);
        var nextMethod = app.IndexOf("private async Task EnsureRelatedRecordsAccessibleAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && nextMethod > methodStart);
        var method = app[methodStart..nextMethod];

        Assert.Contains("[UnitOfWork(true)]", app[..methodStart], StringComparison.Ordinal);
        Assert.Contains("UX_{table}_TeId_Sq_IsDe", entity, StringComparison.Ordinal);
        Assert.Contains("nameof(TenantId)", entity, StringComparison.Ordinal);
        Assert.Contains("nameof(SourceQuoteId)", entity, StringComparison.Ordinal);
        Assert.Contains("nameof(IsDeleted)", entity, StringComparison.Ordinal);
        Assert.Contains("ux_crm_sales_order_teid_sq_isde", migration, StringComparison.Ordinal);
        Assert.Contains("ON crm_sales_order (tenant_id, source_quote_id, is_deleted)", migration, StringComparison.Ordinal);

        var orderInsert = method.IndexOf("salesOrders.AddAsync(order, cancellationToken)", StringComparison.Ordinal);
        var lineInsert = method.IndexOf("salesOrderLines.AddAsync(orderLine, cancellationToken)", StringComparison.Ordinal);
        var quoteLink = method.IndexOf("records.UpdateAsync(quote, cancellationToken)", StringComparison.Ordinal);
        Assert.True(orderInsert >= 0 && lineInsert > orderInsert && quoteLink > lineInsert,
            "The quote link must be persisted only after the order header and all snapshot lines have been staged in the same transaction.");
        Assert.Contains("quote.ConvertedOrderId = createdOrder.BasicId", method, StringComparison.Ordinal);
        Assert.Contains("existingOrder.SourceQuoteId != quote.BasicId", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversionEndpoint_ShouldUseStableQuoteDerivedNumberProtectedByTenantUniqueIndex()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmSalesOrder.cs");
        var methodStart = app.IndexOf("public async Task<CrmSalesOrderListItemDto> ConvertToSalesOrderAsync", StringComparison.Ordinal);
        var methodEnd = app.IndexOf("private async Task EnsureRelatedRecordsAccessibleAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = app[methodStart..methodEnd];

        Assert.Contains("OrderNumber = $" + "\"SO-{quote.BasicId}\"", method, StringComparison.Ordinal);
        Assert.Contains("UX_{table}_TeId_No", entity, StringComparison.Ordinal);
        var indexStart = entity.IndexOf("UX_{table}_TeId_No", StringComparison.Ordinal);
        var indexEnd = entity.IndexOf(")]", indexStart, StringComparison.Ordinal);
        Assert.True(indexEnd > indexStart);
        var declaration = entity[indexStart..indexEnd];
        var tenantKey = declaration.IndexOf("nameof(TenantId)", StringComparison.Ordinal);
        var numberKey = declaration.IndexOf("nameof(OrderNumber)", StringComparison.Ordinal);
        Assert.True(tenantKey >= 0 && numberKey > tenantKey,
            "Sales-order number uniqueness must be tenant-scoped and include the generated order number.");
    }

    [Fact]
    public void ConversionEndpoint_ShouldPreserveQuotationTermsInSalesOrderSnapshotAndUpgradeSchema()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmSalesOrder.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.37", "5.9.37.sql");

        var methodStart = app.IndexOf("public async Task<CrmSalesOrderListItemDto> ConvertToSalesOrderAsync", StringComparison.Ordinal);
        var methodEnd = app.IndexOf("private async Task EnsureRelatedRecordsAccessibleAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = app[methodStart..methodEnd];

        Assert.Contains("TermsAndConditions = quote.TermsAndConditions", method, StringComparison.Ordinal);
        Assert.Contains("ColumnName = \"Terms_And_Conditions\"", entity, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS terms_and_conditions text NULL", migration, StringComparison.Ordinal);
        Assert.Contains("to_regclass('crm_sales_order') IS NOT NULL", migration, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));

    [Fact]
    public void ConversionEndpoint_ShouldRequireContactAndOpportunityToBelongToQuotedCustomer()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        var helperStart = app.IndexOf("private async Task EnsureRelatedRecordsAccessibleAsync", StringComparison.Ordinal);
        var helperEnd = app.IndexOf("private static CrmQuoteDto", helperStart, StringComparison.Ordinal);
        Assert.True(helperStart >= 0 && helperEnd > helperStart);
        var helper = app[helperStart..helperEnd];

        Assert.Contains("contact.CustomerId != customer.BasicId", helper, StringComparison.Ordinal);
        Assert.Contains("Contact must belong to the quoted customer.", helper, StringComparison.Ordinal);
        Assert.Contains("opportunity.CustomerId != customer.BasicId", helper, StringComparison.Ordinal);
        Assert.Contains("Opportunity must belong to the quoted customer.", helper, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, contact.OwnerUserId", helper, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, opportunity.OwnerUserId", helper, StringComparison.Ordinal);
    }


    [Fact]
    public void QuoteAndConvertedOrder_ShouldPreserveContactAndOpportunityNameSnapshots()
    {
        var domain = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "DomainServices", "Implementations", "CrmQuoteDomainService.cs");
        var quoteApp = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        var quoteEntity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmQuote.cs");
        var orderEntity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmSalesOrder.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.38", "5.9.38.sql");

        Assert.Contains("ContactNameSnapshot = contact is null ? null : string.Join", domain, StringComparison.Ordinal);
        Assert.Contains("OpportunityNameSnapshot = opportunity?.Name", domain, StringComparison.Ordinal);
        Assert.Contains("ContactNameSnapshot = quote.ContactNameSnapshot", quoteApp, StringComparison.Ordinal);
        Assert.Contains("OpportunityNameSnapshot = quote.OpportunityNameSnapshot", quoteApp, StringComparison.Ordinal);
        Assert.Contains("ColumnName = \"Opportunity_Name_Snapshot\"", quoteEntity, StringComparison.Ordinal);
        Assert.Contains("ColumnName = \"Contact_Name_Snapshot\"", orderEntity, StringComparison.Ordinal);
        Assert.Contains("ColumnName = \"Opportunity_Name_Snapshot\"", orderEntity, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS opportunity_name_snapshot varchar(200) NULL", migration, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS contact_name_snapshot varchar(200) NULL", migration, StringComparison.Ordinal);
    }

}
