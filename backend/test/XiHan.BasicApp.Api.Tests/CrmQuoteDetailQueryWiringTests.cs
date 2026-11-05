using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmQuoteDetailQueryWiringTests
{
    [Fact]
    public void QuoteDetail_ShouldCheckParentOwnerScopeAndReturnCommercialSnapshot()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmQuoteQueryService.cs");
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", source, StringComparison.Ordinal);
        Assert.Contains("ResolveAccessibleUsersAsync", source, StringComparison.Ordinal);
        Assert.Contains("CanReadOwnedRecord(quote.OwnerUserId)", source, StringComparison.Ordinal);
        Assert.Contains("ConvertedOrderId = quote.ConvertedOrderId", source, StringComparison.Ordinal);
        Assert.Contains("PreviousVersionId = quote.PreviousVersionId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteLines_ShouldBePagedSortedAndFieldGuardedAfterParentAuthorization()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmQuoteQueryService.cs");
        Assert.Contains("x => x.QuoteId, input.QuoteId", source, StringComparison.Ordinal);
        Assert.Contains("GuardQueryAsync(request.Conditions, typeof(SysCrmQuoteLine)", source, StringComparison.Ordinal);
        Assert.Contains("quoteLines.GetPagedAsync(request, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("SortOrder", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));
    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
