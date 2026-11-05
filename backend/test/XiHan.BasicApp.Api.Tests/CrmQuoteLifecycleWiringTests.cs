using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level guards for quotation lifecycle rules and authorization.</summary>
public sealed class CrmQuoteLifecycleWiringTests
{
    [Fact]
    public void QuoteStatusChange_ShouldRequireExecutePermissionAndRecordScope()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", app, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync", app, StringComparison.Ordinal);
        Assert.Contains("EnsureUpdatableAsync(typeof(SysCrmQuote)", app, StringComparison.Ordinal);
        Assert.Contains("[UnitOfWork(true)]", app, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteStatusChange_ShouldOnlyAllowDefinedLifecycleTransitions()
    {
        var domain = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "DomainServices", "Implementations", "CrmQuoteDomainService.cs");
        Assert.Contains("CrmQuoteStatus.Draft => target == CrmQuoteStatus.Sent", domain, StringComparison.Ordinal);
        Assert.Contains("target is CrmQuoteStatus.Accepted or CrmQuoteStatus.Rejected", domain, StringComparison.Ordinal);
        Assert.Contains("target == CrmQuoteStatus.Expired && quote.ValidUntil.HasValue", domain, StringComparison.Ordinal);
        Assert.Contains("An expired quote cannot be accepted or rejected.", domain, StringComparison.Ordinal);
        Assert.Contains("Quote status transition", domain, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
