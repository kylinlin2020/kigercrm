using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmQuoteRevisionWiringTests
{
    [Fact]
    public void RevisionCommand_ShouldBeCreateAuthorizedTransactionalAndOwnerScoped()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        Assert.Contains("[UnitOfWork(true)]", app, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]", app, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync", app, StringComparison.Ordinal);
        Assert.Contains("domainService.CreateRevisionAsync", app, StringComparison.Ordinal);
    }

    [Fact]
    public void RevisionDomain_ShouldCreateNextVersionAndPreserveQuoteIdentity()
    {
        var domain = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "DomainServices", "Implementations", "CrmQuoteDomainService.cs");
        Assert.Contains("checked(source.Version + 1)", domain, StringComparison.Ordinal);
        Assert.Contains("PreviousVersionId = source.BasicId", domain, StringComparison.Ordinal);
        Assert.Contains("QuoteNumber = source.QuoteNumber", domain, StringComparison.Ordinal);
        Assert.Contains("CustomerId = source.CustomerId", domain, StringComparison.Ordinal);
        Assert.Contains("CrmQuoteStatus.Accepted", domain, StringComparison.Ordinal);
        Assert.Contains("CrmQuoteStatus.Converted", domain, StringComparison.Ordinal);
    }


    [Fact]
    public void RevisionDomain_ShouldKeepSourceImmutableAndCreateFreshDraftLines()
    {
        var domain = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "DomainServices", "Implementations", "CrmQuoteDomainService.cs");
        Assert.Contains("var revision = command with", domain, StringComparison.Ordinal);
        Assert.Contains("return await CreateAsync(revision, cancellationToken)", domain, StringComparison.Ordinal);
        Assert.Contains("Status = CrmQuoteStatus.Draft", domain, StringComparison.Ordinal);
        Assert.Contains("foreach (var line in calculated)", domain, StringComparison.Ordinal);
        Assert.Contains("line.QuoteId = quote.BasicId", domain, StringComparison.Ordinal);
        Assert.Contains("await quoteLines.AddAsync(line, cancellationToken)", domain, StringComparison.Ordinal);
    }

    [Fact]
    public void RevisionEndpoint_ShouldValidateSourceOwnerAndFieldSecurity()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmQuoteAppService.cs");
        Assert.Contains("records.GetByIdAsync(sourceQuoteId, cancellationToken)", app, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, source.OwnerUserId", app, StringComparison.Ordinal);
        Assert.Contains("EnsureAssignableAsync(dataScope, source.OwnerUserId", app, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmQuoteLine)", app, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));
    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
