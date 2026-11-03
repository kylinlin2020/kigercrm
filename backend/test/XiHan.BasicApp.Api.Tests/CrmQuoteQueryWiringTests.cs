using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level guards for quotation list query authorization and data scope.</summary>
public sealed class CrmQuoteQueryWiringTests
{
    [Fact]
    public void QuotePageQuery_ShouldRequireReadPermissionAndApplyOwnerAndFieldScopes()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmQuoteQueryService.cs");
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", source, StringComparison.Ordinal);
        Assert.Contains("ApplyOwnerScopeAsync", source, StringComparison.Ordinal);
        Assert.Contains("GuardQueryAsync(request.Conditions, typeof(SysCrmQuote)", source, StringComparison.Ordinal);
        Assert.Contains("repository.GetPagedAsync(request, cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuotePageQuery_ShouldValidateFiltersAndExposeCommercialSummary()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmQuoteQueryService.cs");
        Assert.Contains("Enum.IsDefined(typeof(CrmQuoteStatus), input.Status.Value)", source, StringComparison.Ordinal);
        Assert.Contains("x.QuoteNumber, x => x.CustomerNameSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("x => x.CustomerId", source, StringComparison.Ordinal);
        var dto = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmQuoteQueryDtos.cs");
        Assert.Contains("GrandTotal", dto, StringComparison.Ordinal);
        Assert.Contains("ValidUntil", dto, StringComparison.Ordinal);
        Assert.Contains("Version", dto, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
