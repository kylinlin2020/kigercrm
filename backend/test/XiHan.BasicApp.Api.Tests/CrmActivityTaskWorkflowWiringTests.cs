using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Protects the Activity and Task write workflows against regressions in assignment scope,
/// field-security enforcement, and related-record validation.
/// These are source-wiring guards; provider-backed integration tests remain necessary.
/// </summary>
public sealed class CrmActivityTaskWorkflowWiringTests
{
    [Fact]
    public void ActivityCommands_ShouldEnforceOwnerScopeAndFieldSecurity()
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "AppServices", "CrmActivityAppService.cs");

        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]", source, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Update)]", source, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Delete)]", source, StringComparison.Ordinal);
        Assert.Contains("EnsureAssignableAsync(dataScope, input.OwnerUserId", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, existing.OwnerUserId", source, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmActivity)", source, StringComparison.Ordinal);
        Assert.Contains("EnsureUpdatableAsync(typeof(SysCrmActivity)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskCommands_ShouldEnforceAssigneeScopeAndFieldSecurityForStatusChanges()
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "AppServices", "CrmTaskAppService.cs");

        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]", source, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Update)]", source, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Delete)]", source, StringComparison.Ordinal);
        Assert.Contains("EnsureAssignableAsync(dataScope, input.AssigneeUserId", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, existing.AssigneeUserId", source, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmTask)", source, StringComparison.Ordinal);
        Assert.Equal(2, Count(source, "EnsureUpdatableAsync(typeof(SysCrmTask)"));
    }

    [Fact]
    public void ActivityAndTaskDomains_ShouldValidateRelatedRecordsInCurrentTenant()
    {
        var activity = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain",
            "DomainServices", "Implementations", "CrmActivityDomainService.cs");
        var task = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain",
            "DomainServices", "Implementations", "CrmTaskDomainService.cs");

        foreach (var entityType in new[] { "customer", "contact", "lead", "opportunity" })
        {
            Assert.Contains("\"" + entityType + "\" => await ", activity, StringComparison.Ordinal);
            Assert.Contains("\"" + entityType + "\" => await ", task, StringComparison.Ordinal);
        }

        Assert.Contains("Related CRM record does not exist in the current tenant.", activity, StringComparison.Ordinal);
        Assert.Contains("Related CRM record does not exist in the current tenant.", task, StringComparison.Ordinal);
        Assert.Contains("Related entity type and positive id must be supplied together.", task, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskCompletionTimestamp_ShouldTrackCompletedStateAndClearWhenReopened()
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain",
            "DomainServices", "Implementations", "CrmTaskDomainService.cs");

        Assert.Contains("entity.CompletedAt = entity.Status == CrmTaskStatus.Completed ? entity.CompletedAt ?? DateTime.UtcNow : null;", source, StringComparison.Ordinal);
        Assert.Contains("entity.Status = (CrmTaskStatus)status;", source, StringComparison.Ordinal);
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
