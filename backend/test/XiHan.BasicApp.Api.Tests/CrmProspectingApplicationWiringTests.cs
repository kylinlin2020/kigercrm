using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level guards for prospecting application workflow boundaries.</summary>
public sealed class CrmProspectingApplicationWiringTests
{
    [Fact]
    public void ProspectingApplication_ShouldExposeCampaignAndStrategyLifecycle()
    {
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Contracts", "ICrmProspectingAppService.cs");
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmProspectingAppService.cs");
        foreach (var operation in new[] { "CreateCampaignAsync", "UpdateCampaignAsync", "GetCampaignAsync", "CreateStrategyVersionAsync", "SubmitStrategyForApprovalAsync", "DecideStrategyAsync", "ActivateStrategyAsync" })
        {
            Assert.Contains(operation, contract, StringComparison.Ordinal);
            Assert.Contains(operation, service, StringComparison.Ordinal);
        }
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Update)]", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingDomain_ShouldRequireApprovalBeforeActivationAndValidateProviderJson()
    {
        var domain = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "DomainServices", "Implementations", "CrmProspectingDomainService.cs");
        Assert.Contains("Only an approved strategy can be activated.", domain, StringComparison.Ordinal);
        Assert.Contains("Only a pending strategy can receive an approval decision.", domain, StringComparison.Ordinal);
        Assert.Contains("JsonDocument.Parse", domain, StringComparison.Ordinal);
        Assert.Contains("GetListAsync", domain, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
