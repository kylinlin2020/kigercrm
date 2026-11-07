using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level regression guards for PostgreSQL extraction claims and fenced writes.</summary>
public sealed class CrmProspectingExtractionRepositoryContractTests
{
    private const string RepositoryPath =
        "backend/src/modules/XiHan.BasicApp.Crm/Infrastructure/Repositories/CrmProspectingExtractionJobRepository.cs";

    [Fact]
    public void ClaimDue_ShouldUseDatabaseTimeSkipLockedAndMonotonicFencing()
    {
        var source = Read(RepositoryPath);
        var start = source.IndexOf("public async Task<IReadOnlyList<SysCrmProspectingExtractionJob>> ClaimDueAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<bool> TryRenewLeaseAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("FOR UPDATE SKIP LOCKED", method, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CURRENT_TIMESTAMP", method, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FencingVersion", method, StringComparison.Ordinal);
        Assert.Contains("LeaseToken", method, StringComparison.Ordinal);
        Assert.Contains("TenantId", method, StringComparison.Ordinal);
        Assert.DoesNotContain("NeedsReview", method, StringComparison.Ordinal);
    }

    [Fact]
    public void LeaseRenewal_ShouldNotResurrectExpiredOrSupersededLease()
    {
        var source = Read(RepositoryPath);
        var start = source.IndexOf("public async Task<bool> TryRenewLeaseAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<int> ExpireDueAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("LeaseToken", method, StringComparison.Ordinal);
        Assert.Contains("FencingVersion", method, StringComparison.Ordinal);
        Assert.Contains("LeaseExpiresAtUtc", method, StringComparison.Ordinal);
        Assert.Contains("CURRENT_TIMESTAMP", method, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TenantId", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CursorAdvance_ShouldRequireLiveLeaseExpectedCursorAndBoundedForwardProgress()
    {
        var source = Read(RepositoryPath);
        var start = source.IndexOf("public async Task<bool> TryAdvanceCursorAsync", StringComparison.Ordinal);
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("LeaseToken", method, StringComparison.Ordinal);
        Assert.Contains("FencingVersion", method, StringComparison.Ordinal);
        Assert.Contains("LeaseExpiresAtUtc", method, StringComparison.Ordinal);
        Assert.Contains("expectedCursor", method, StringComparison.Ordinal);
        Assert.Contains("nextCursor", method, StringComparison.Ordinal);
        Assert.Contains("MaxCursorLength", method, StringComparison.Ordinal);
        Assert.Contains("MaxRecordsPerPage", method, StringComparison.Ordinal);
        Assert.Contains("TenantId", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractionEntity_ShouldBeStrictlyTenantScopedAndUniquelyIdentified()
    {
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Domain", "Entities", "SysCrmProspectingExtractionJob.cs");
        Assert.Contains("IStrictMultiTenantEntity", entity, StringComparison.Ordinal);
        Assert.Contains("nameof(TenantId)", entity, StringComparison.Ordinal);
        Assert.Contains("nameof(RunId)", entity, StringComparison.Ordinal);
        Assert.Contains("nameof(ProviderCode)", entity, StringComparison.Ordinal);
        Assert.Contains("nameof(RequestFingerprint)", entity, StringComparison.Ordinal);
        Assert.Contains("Fencing_Version", entity, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
