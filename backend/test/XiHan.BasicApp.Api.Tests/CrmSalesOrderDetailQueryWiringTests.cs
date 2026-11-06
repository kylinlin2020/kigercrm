using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmSalesOrderDetailQueryWiringTests
{
    [Fact]
    public void SalesOrderDetail_ShouldEnforceParentOwnerScopeAndExposeTerms()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmSalesOrderQueryService.cs");
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", source, StringComparison.Ordinal);
        Assert.Contains("ResolveAccessibleUsersAsync", source, StringComparison.Ordinal);
        Assert.Contains("CanReadOwnedRecord(order.OwnerUserId)", source, StringComparison.Ordinal);
        Assert.Contains("CustomerPurchaseOrderNumber = order.CustomerPurchaseOrderNumber", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesOrderDetail_ShouldExposePersistedTermsAndConditionsSnapshot()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmSalesOrderQueryService.cs");
        var dto = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmSalesOrderQueryDtos.cs");

        Assert.Contains("public string? TermsAndConditions { get; set; }", dto, StringComparison.Ordinal);
        Assert.Contains("TermsAndConditions = order.TermsAndConditions", source, StringComparison.Ordinal);
        Assert.Contains("CustomerPurchaseOrderNumber = order.CustomerPurchaseOrderNumber", source, StringComparison.Ordinal);
        Assert.Contains("PaymentTerms = order.PaymentTerms", source, StringComparison.Ordinal);
        Assert.Contains("DeliveryTerms = order.DeliveryTerms", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesOrderLines_ShouldBePagedSortedAndFieldGuardedAfterParentAuthorization()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmSalesOrderQueryService.cs");
        Assert.Contains("x => x.SalesOrderId, input.SalesOrderId", source, StringComparison.Ordinal);
        Assert.Contains("GuardQueryAsync(request.Conditions, typeof(SysCrmSalesOrderLine)", source, StringComparison.Ordinal);
        Assert.Contains("orderLines.GetPagedAsync(request, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("SortOrder", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));
    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));

    [Fact]
    public void SalesOrderCreateAndDraftUpdate_ShouldPersistTermsAndConditions()
    {
        var createDto = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmSalesOrderCreateDto.cs");
        var updateDto = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmSalesOrderUpdateDto.cs");
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmSalesOrderAppService.cs");
        var frontendTypes = Read("frontend", "src", "api", "modules", "crm", "sales-order.types.ts");
        var frontendView = Read("frontend", "src", "modules", "crm", "views", "crm", "sales-order", "index.vue");

        Assert.Contains("public string? TermsAndConditions { get; set; }", createDto, StringComparison.Ordinal);
        Assert.Contains("public string? TermsAndConditions { get; set; }", updateDto, StringComparison.Ordinal);
        Assert.Contains("TermsAndConditions = termsAndConditions", service, StringComparison.Ordinal);
        Assert.Contains("order.TermsAndConditions = NormalizeOptional(input.TermsAndConditions, 20000", service, StringComparison.Ordinal);
        Assert.Contains("termsAndConditions?: string | null", frontendTypes, StringComparison.Ordinal);
        Assert.Contains("termsAndConditions:form.value.termsAndConditions", frontendView, StringComparison.Ordinal);
        Assert.Contains("termsAndConditions:editForm.value.termsAndConditions", frontendView, StringComparison.Ordinal);
    }
}
