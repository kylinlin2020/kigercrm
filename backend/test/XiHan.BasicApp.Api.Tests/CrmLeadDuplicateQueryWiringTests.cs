using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Guards the security and bounded-query wiring of the Lead duplicate candidate API.
/// Database-backed authorization and tenant-isolation tests are still required.
/// </summary>
public sealed class CrmLeadDuplicateQueryWiringTests
{
    [Fact]
    public void DuplicateQuery_ShouldRequireReadPermissionAndApplyTenantOwnerAndFieldScopes()
    {
        var source = File.ReadAllText(RepositoryPath(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "QueryServices", "CrmLeadDuplicateQueryService.cs"));

        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", source, StringComparison.Ordinal);
        Assert.Contains("repository.GetPagedAsync(request, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("ApplyOwnerScopeAsync(", source, StringComparison.Ordinal);
        Assert.Contains("fieldSecurity.GuardQueryAsync(", source, StringComparison.Ordinal);
        Assert.Contains("new PageRequestMetadata(1, CandidatePageSize)", source, StringComparison.Ordinal);
        Assert.Contains("private const int CandidatePageSize = 100;", source, StringComparison.Ordinal);
        Assert.Contains("private const int MaximumReturnedCandidates = 50;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateQuery_ShouldUnionSignalsAndDeduplicateByLeadId()
    {
        var source = File.ReadAllText(RepositoryPath(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "QueryServices", "CrmLeadDuplicateQueryService.cs"));

        Assert.Contains("var candidates = new Dictionary<long, SysCrmLead>();", source, StringComparison.Ordinal);
        Assert.Contains("candidates.TryAdd(candidate.BasicId, candidate)", source, StringComparison.Ordinal);
        Assert.Contains("lead.DuplicateEmailNormalized", source, StringComparison.Ordinal);
        Assert.Contains("lead.PhoneDigits", source, StringComparison.Ordinal);
        Assert.Contains("lead.WebsiteHost", source, StringComparison.Ordinal);
        Assert.Contains("SetKeyword<SysCrmLead>(company, lead => lead.CompanyName)", source, StringComparison.Ordinal);
        Assert.Contains("CrmLeadDuplicateMatcher.FindMatches(source, candidates.Values, 50)", source, StringComparison.Ordinal);
        Assert.Contains(".Take(MaximumReturnedCandidates)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateQuery_ShouldValidateInputsAndExcludeSourceLead()
    {
        var source = File.ReadAllText(RepositoryPath(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "QueryServices", "CrmLeadDuplicateQueryService.cs"));

        Assert.Contains("ValidateLength(input.CompanyName, 200", source, StringComparison.Ordinal);
        Assert.Contains("ValidateLength(input.Email, 254", source, StringComparison.Ordinal);
        Assert.Contains("ValidateLength(input.Phone, 40", source, StringComparison.Ordinal);
        Assert.Contains("ValidateLength(input.Website, 500", source, StringComparison.Ordinal);
        Assert.Contains("if (candidate.BasicId != source.BasicId)", source, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", source, StringComparison.Ordinal);
    }

    private static string RepositoryPath(params string[] segments) =>
        Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments)));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
