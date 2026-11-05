using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingExtractionPersistenceContractTests
{
    [Fact]
    public void ExtractionJob_ShouldBeStrictlyTenantScoped()
        => Assert.Contains(typeof(IStrictMultiTenantEntity),
            typeof(SysCrmProspectingExtractionJob).GetInterfaces());

    [Fact]
    public void Repository_ShouldExposeDurableClaimExpiryAndCursorOperations()
    {
        var methods = typeof(ICrmProspectingExtractionJobRepository).GetMethods()
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("CreateSubmissionIntentAsync", methods);
        var directCreate = typeof(ICrmProspectingExtractionJobRepository)
            .GetMethod("CreateAsync");
        Assert.NotNull(directCreate);
        Assert.NotNull(Attribute.GetCustomAttribute(directCreate!, typeof(ObsoleteAttribute)));
        Assert.Contains("TryAcknowledgeSubmissionAsync", methods);
        Assert.Contains("GetByIdAsync", methods);
        Assert.Contains("ClaimDueAsync", methods);
        Assert.Contains("TryRenewLeaseAsync", methods);
        Assert.Contains("ExpireDueAsync", methods);
        Assert.Contains("TryTransitionAsync", methods);
        Assert.Contains("TryCompleteResultsAsync", methods);
        Assert.Contains("TryAdvanceCursorAsync", methods);
    }

    [Fact]
    public void ExtractionLeaseRenewal_ShouldRequireExactJobLeaseAndFencingIdentity()
    {
        var method = typeof(ICrmProspectingExtractionJobRepository).GetMethod("TryRenewLeaseAsync");
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(
            new[] { typeof(long), typeof(Guid), typeof(long), typeof(TimeSpan), typeof(CancellationToken) },
            parameters.Select(parameter => parameter.ParameterType).ToArray());
        Assert.Equal(typeof(Task<bool>), method.ReturnType);
    }

    [Fact]
    public void RemoteProviderStatuses_ShouldNotBePersistedLifecycleStatuses()
    {
        Assert.NotEqual(
            typeof(CrmProspectingExtractionStatus),
            typeof(CrmProspectingExtractionProviderStatus));
        Assert.DoesNotContain(
            Enum.GetNames<CrmProspectingExtractionProviderStatus>(),
            name => name == nameof(CrmProspectingExtractionStatus.FetchingResults)
                || name == nameof(CrmProspectingExtractionStatus.Completed));
    }

    [Fact]
    public void ExtractionJob_ShouldCarryTenantRunFingerprintAndFencingFields()
    {
        var properties = typeof(SysCrmProspectingExtractionJob).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(SysCrmProspectingExtractionJob.TenantId), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.RunId), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.RequestFingerprint), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.SubmissionIdempotencyKey), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.ProviderExtractionId), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.NextPollAtUtc), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.ResultCursor), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.LeaseToken), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.FencingVersion), properties);
    }

    [Fact]
    public void CandidatePersistence_ShouldExposeAtomicProviderBatchAndTerminalTransitionBoundary()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingCandidateFactory.cs"));
        var methodStart = source.IndexOf("PersistProviderBatchAndTransitionAsync", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodEnd = source.IndexOf("PersistPageAndAdvanceAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.Contains("_providerExecutions.GetByRunAsync", method, StringComparison.Ordinal);
        Assert.Contains("execution.CheckpointHash", method, StringComparison.Ordinal);
        Assert.Contains("PersistCandidateOrRepairAsync", method, StringComparison.Ordinal);
        Assert.Contains("\"Projecting\", \"Succeeded\"", method, StringComparison.Ordinal);
        Assert.Contains("\"Projecting\", \"Failed\"", method, StringComparison.Ordinal);
        Assert.Contains("unitOfWork.CompleteAsync", method, StringComparison.Ordinal);
        Assert.Contains("_providerExecutions", source, StringComparison.Ordinal);
    }
    [Fact]
    public void AtomicProviderBatch_ShouldRejectDuplicateNormalizedRecordIdentitiesBeforePersistence()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingCandidateFactory.cs"));
        var methodStart = source.IndexOf("PersistProviderBatchAndTransitionAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("PersistPageAndAdvanceAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.Contains("new HashSet<string>(StringComparer.Ordinal)", method, StringComparison.Ordinal);
        Assert.Contains("if (!batchIdentities.Add(normalizedIdentity))", method, StringComparison.Ordinal);
        Assert.True(method.IndexOf("if (!batchIdentities.Add(normalizedIdentity))", StringComparison.Ordinal) <
            method.IndexOf("await PersistCandidateOrRepairAsync(item, cancellationToken)", StringComparison.Ordinal));
    }

    [Fact]
    public void AtomicProviderBatch_ShouldVerifyCheckpointBytesHashAndRecordMembership()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingCandidateFactory.cs"));
        var methodStart = source.IndexOf("PersistProviderBatchAndTransitionAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("PersistPageAndAdvanceAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.Contains("execution.CheckpointBytes != System.Text.Encoding.UTF8.GetByteCount(execution.CheckpointJson)", method, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(execution.CheckpointJson))", method, StringComparison.Ordinal);
        Assert.Contains("persistedCheckpoint.Records.Count != execution.RawRecordCount", method, StringComparison.Ordinal);
        Assert.Contains("if (!checkpointIdentities.Contains(normalizedIdentity))", method, StringComparison.Ordinal);
    }

}
