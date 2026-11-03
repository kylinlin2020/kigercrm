using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmOwnerTransferAuthorizationTests
{
    [Fact]
    public void OwnerChange_ShouldRequireAdditionalExecutePermission()
    {
        var helper = ReadSource("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmAssignmentAuthorization.cs");
        Assert.Contains("EnsureTransferPermissionAsync", helper, StringComparison.Ordinal);
        Assert.Contains("if (currentUserId != targetUserId)", helper, StringComparison.Ordinal);
        Assert.Contains("CrmPermissionCodes.Execute", helper, StringComparison.Ordinal);
        Assert.Contains("permissionGuard.EnsureGrantedAsync", helper, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CrmCustomerAppService.cs", "existing.OwnerUserId", "input.OwnerUserId")]
    [InlineData("CrmContactAppService.cs", "existing.OwnerUserId", "input.OwnerUserId")]
    [InlineData("CrmLeadAppService.cs", "existing.OwnerUserId", "input.OwnerUserId")]
    [InlineData("CrmOpportunityAppService.cs", "existing.OwnerUserId", "input.OwnerUserId")]
    [InlineData("CrmActivityAppService.cs", "existing.OwnerUserId", "input.OwnerUserId")]
    [InlineData("CrmTaskAppService.cs", "existing.AssigneeUserId", "input.AssigneeUserId")]
    public void AssignmentUpdate_ShouldApplyTransferPermissionAndScopeChecks(string fileName, string currentOwner, string targetOwner)
    {
        var source = ReadSource("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", fileName);
        var guard = $"EnsureTransferPermissionAsync(permissionGuard, {currentOwner}, {targetOwner}, cancellationToken)";
        Assert.Contains(guard, source, StringComparison.Ordinal);
        Assert.Contains("EnsureAssignableAsync(dataScope, " + targetOwner + ", cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("IOperationPermissionGuard permissionGuard", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(segments)));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
