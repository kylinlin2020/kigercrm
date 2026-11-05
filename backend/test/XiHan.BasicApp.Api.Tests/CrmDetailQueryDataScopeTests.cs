using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Prevents CRM detail and child-line endpoints from bypassing mandatory owner/assignee data scope.
/// </summary>
public sealed class CrmDetailQueryDataScopeTests
{
    [Fact]
    public void CrmDetailQueries_ShouldEnforceOwnerOrAssigneeScope()
    {
        var queryServices = new[]
        {
            "CrmActivityQueryService.cs",
            "CrmContactQueryService.cs",
            "CrmCustomerQueryService.cs",
            "CrmLeadQueryService.cs",
            "CrmOpportunityQueryService.cs",
            "CrmQuoteQueryService.cs",
            "CrmSalesOrderQueryService.cs",
            "CrmShipmentQueryService.cs",
            "CrmTaskQueryService.cs"
        };

        foreach (var fileName in queryServices)
        {
            var source = Read(
                "backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
                "QueryServices", fileName);
            Assert.Contains("GetDetailAsync(", source, StringComparison.Ordinal);
            Assert.Contains("ResolveAccessibleUsersAsync(", source, StringComparison.Ordinal);
            Assert.Contains("CanReadOwnedRecord(", source, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("CrmQuoteQueryService.cs", "Quote is outside the current data scope.")]
    [InlineData("CrmSalesOrderQueryService.cs", "Sales order is outside the current data scope.")]
    [InlineData("CrmShipmentQueryService.cs", "Shipment is outside the current data scope.")]
    public void ChildLineQueries_ShouldCheckParentRecordScope(string fileName, string expectedMessage)
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "QueryServices", fileName);
        Assert.Contains("GetLinesPageAsync(", source, StringComparison.Ordinal);
        Assert.Contains("CanReadOwnedRecord(", source, StringComparison.Ordinal);
        Assert.Contains(expectedMessage, source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
