using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Prevents field-security query guards from deleting mandatory owner/assignee scope filters.
/// User-controlled conditions must be guarded first; server-enforced data scope is appended last.
/// </summary>
public sealed class CrmQuerySecurityOrderingTests
{
    [Fact]
    public void CrmListQueries_ShouldApplyMandatoryOwnerScopeAfterFieldSecurityGuard()
    {
        var queryServices = new[]
        {
            "CrmActivityQueryService.cs",
            "CrmContactQueryService.cs",
            "CrmCustomerQueryService.cs",
            "CrmLeadQueryService.cs",
            "CrmLeadDuplicateQueryService.cs",
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
            var fieldGuard = source.IndexOf("await fieldSecurity.GuardQueryAsync(", StringComparison.Ordinal);
            var ownerScope = source.IndexOf("await request.Conditions.ApplyOwnerScopeAsync(", StringComparison.Ordinal);

            Assert.True(fieldGuard >= 0, $"{fileName} must guard user-controlled query conditions.");
            Assert.True(ownerScope >= 0, $"{fileName} must apply mandatory owner/assignee data scope.");
            Assert.True(
                fieldGuard < ownerScope,
                $"{fileName} must apply owner/assignee data scope after the field-security guard so protected-field cleanup cannot remove the mandatory scope.");
        }
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
