using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level regression guards for durable prospecting candidate projection.</summary>
public sealed class CrmProspectingCandidatePersistenceContractTests
{
    private const string CandidateFactoryPath =
        "backend/src/modules/XiHan.BasicApp.Crm/Infrastructure/Prospecting/CrmProspectingCandidateFactory.cs";

    [Fact]
    public void AsyncPageProjection_ShouldBeOneTenantScopedAtomicTransaction()
    {
        var source = Read(CandidateFactoryPath);
        var start = source.IndexOf("public async Task<int> PersistPageAndAdvanceAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task PersistCandidateOrRepairAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "The async page persistence boundary must remain explicit.");
        var method = source[start..end];

        Assert.Contains("Begin(", method, StringComparison.Ordinal);
        Assert.Contains("isTransactional: true", method, StringComparison.Ordinal);
        Assert.Contains("requiresNew: true", method, StringComparison.Ordinal);
        Assert.Contains("_extractionJobs.GetByIdAsync(extractionJobId", method, StringComparison.Ordinal);
        Assert.Contains("extraction.LeaseToken != extractionLeaseToken", method, StringComparison.Ordinal);
        Assert.Contains("extraction.FencingVersion != extractionFencingVersion", method, StringComparison.Ordinal);
        Assert.Contains("PersistCandidateOrRepairAsync(item", method, StringComparison.Ordinal);
        Assert.Contains("TryAdvanceCursorAsync(", method, StringComparison.Ordinal);
        Assert.Contains("if (!advanced)", method, StringComparison.Ordinal);
        Assert.Contains("await unitOfWork.CompleteAsync(cancellationToken)", method, StringComparison.Ordinal);

        var failedCas = method.IndexOf("if (!advanced)", StringComparison.Ordinal);
        var commit = method.IndexOf("await unitOfWork.CompleteAsync", StringComparison.Ordinal);
        Assert.True(failedCas >= 0 && commit > failedCas,
            "Cursor CAS must succeed before the transaction can commit.");
    }

    [Fact]
    public void AsyncPageProjection_ShouldRejectPartialOrAmbiguousRecordMapping()
    {
        var source = Read(CandidateFactoryPath);
        var start = source.IndexOf("public async Task<int> PersistPageAndAdvanceAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task PersistCandidateOrRepairAsync", start, StringComparison.Ordinal);
        var method = source[start..end];

        Assert.Contains("items.Count + page.FailureCount != page.Records.Count", method, StringComparison.Ordinal);
        Assert.Contains("NormalizeExternalId", method, StringComparison.Ordinal);
        Assert.Contains("pageIdentities.Contains(identity)", method, StringComparison.Ordinal);
        Assert.Contains("itemIdentities.Add(identity)", method, StringComparison.Ordinal);
        Assert.Contains("item.Candidate.RunId != extraction.RunId", method, StringComparison.Ordinal);
        Assert.Contains("item.Candidate.ProviderCode", method, StringComparison.Ordinal);
        Assert.Contains("item.Candidate.ExternalId, identity", method, StringComparison.Ordinal);
        Assert.Contains("page.IsLastPage && extraction.ResultFailureCount + page.FailureCount > 0", method, StringComparison.Ordinal);
        Assert.Contains("TryFailIncompleteResultsAsync(", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateRepair_ShouldNotReplaceExistingReviewOrConversionState()
    {
        var source = Read(CandidateFactoryPath);
        var start = source.IndexOf("private async Task PersistCandidateOrRepairAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var method = source[start..];

        Assert.Contains("var target = existing ?? await _candidates.AddAsync(candidate, cancellationToken)", method, StringComparison.Ordinal);
        Assert.Contains("GetByCandidateAsync(target.BasicId", method, StringComparison.Ordinal);
        Assert.Contains("GetLatestByCandidateAsync(target.BasicId, 1", method, StringComparison.Ordinal);
        Assert.Contains("if (existingScores.Length > 0) return", method, StringComparison.Ordinal);
        Assert.DoesNotContain("existing.Status =", method, StringComparison.Ordinal);
        Assert.DoesNotContain("existing.Review", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateFactory_ShouldBoundExternalTextAndNormalizeProviderFields()
    {
        var source = Read(CandidateFactoryPath);
        var start = source.IndexOf("public static class CrmProspectingCandidateFactory", StringComparison.Ordinal);
        var end = source.IndexOf("/// <summary>One already-normalized candidate", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var factory = source[start..end];

        Assert.Contains("NormalizeEmail", factory, StringComparison.Ordinal);
        Assert.Contains("NormalizeDomain", factory, StringComparison.Ordinal);
        Assert.Contains("email?.Length > 254", factory, StringComparison.Ordinal);
        Assert.Contains("website?.Length > 500", factory, StringComparison.Ordinal);
        Assert.Contains("source.Length, 200", factory, StringComparison.Ordinal);
        Assert.Contains("title.Length, 300", factory, StringComparison.Ordinal);
        Assert.Contains("contactName.Length, 200", factory, StringComparison.Ordinal);
        Assert.Contains("summary.Length, 5000", factory, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingCandidateStatus.PendingReview", factory, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
