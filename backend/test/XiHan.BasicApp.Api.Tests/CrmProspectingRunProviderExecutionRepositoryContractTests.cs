using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingRunProviderExecutionRepositoryContractTests
{
    [Fact]
    public void ProviderExecution_ShouldBeStrictlyTenantScopedAndCarryImmutablePlanIdentity()
    {
        Assert.Contains(typeof(IStrictMultiTenantEntity),
            typeof(SysCrmProspectingRunProviderExecution).GetInterfaces());
        var properties = typeof(SysCrmProspectingRunProviderExecution).GetProperties()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var property in new[]
        {
            nameof(SysCrmProspectingRunProviderExecution.TenantId),
            nameof(SysCrmProspectingRunProviderExecution.RunId),
            nameof(SysCrmProspectingRunProviderExecution.PlanVersion),
            nameof(SysCrmProspectingRunProviderExecution.PlanOrdinal),
            nameof(SysCrmProspectingRunProviderExecution.ProviderCode),
            nameof(SysCrmProspectingRunProviderExecution.RequestFingerprint),
            nameof(SysCrmProspectingRunProviderExecution.CheckpointJson),
            nameof(SysCrmProspectingRunProviderExecution.CheckpointHash)
        })
            Assert.Contains(property, properties);
    }

    [Fact]
    public void RepositoryContract_ShouldExposeFencedStartCheckpointAndTransitionOperations()
    {
        var methods = typeof(ICrmProspectingRunProviderExecutionRepository).GetMethods()
            .Select(method => method.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(nameof(ICrmProspectingRunProviderExecutionRepository.GetByRunAsync), methods);
        Assert.Contains(nameof(ICrmProspectingRunProviderExecutionRepository.TryStartAsync), methods);
        Assert.Contains(nameof(ICrmProspectingRunProviderExecutionRepository.TrySaveCheckpointAsync), methods);
        Assert.Contains(nameof(ICrmProspectingRunProviderExecutionRepository.TryTransitionAsync), methods);
        Assert.Contains(nameof(ICrmProspectingRunProviderExecutionRepository.TryMarkInterruptedNeedsReviewAsync), methods);
    }

    [Fact]
    public void Repository_ShouldMaterializeProviderPlanFromImmutableRunSnapshotIdempotently()
    {
        var contract = typeof(ICrmProspectingRunProviderExecutionRepository).GetMethods()
            .Select(method => method.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(nameof(ICrmProspectingRunProviderExecutionRepository.EnsurePlanMaterializedAsync), contract);

        var source = ReadRepository();
        Assert.Contains("run.SnapshotProviderPlanJson", source, StringComparison.Ordinal);
        Assert.Contains("run.StrategyVersion", source, StringComparison.Ordinal);
        Assert.Contains("run.PayloadHash", source, StringComparison.Ordinal);
        Assert.Contains("CreatePlanFingerprint", source, StringComparison.Ordinal);
        Assert.Contains("A partially materialized provider plan was found", source, StringComparison.Ordinal);
        Assert.Contains("refusing to mutate an immutable plan", source, StringComparison.Ordinal);
        Assert.Contains("client.Ado.BeginTran()", source, StringComparison.Ordinal);
        Assert.Contains("client.Ado.CommitTran()", source, StringComparison.Ordinal);
        Assert.Contains("client.Ado.RollbackTran()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RunWorker_ShouldLoadProviderCodesFromPersistedPlanRows()
    {
        var worker = ReadWorker();
        Assert.Contains("EnsurePlanMaterializedAsync", worker, StringComparison.Ordinal);
        Assert.Contains("persistedPlan.OrderBy(step => step.PlanOrdinal)", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("ParseStringArray(run.SnapshotProviderPlanJson, \"provider plan\")", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderExecutionMutations_ShouldUseTheSameLockedRunFence()
    {
        var source = ReadRepository();
        Assert.Contains("WITH locked_run AS MATERIALIZED", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("EXISTS (SELECT 1 FROM locked_run)", source, StringComparison.Ordinal);
        Assert.Contains("step.state = 'Queued'", source, StringComparison.Ordinal);
        Assert.Contains("step.state = 'Started'", source, StringComparison.Ordinal);
        Assert.Contains("SET state = 'NeedsReview'", source, StringComparison.Ordinal);
        Assert.Contains("expectedState is not (\\"Started\\" or \\"Checkpointed\\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_ShouldOwnCheckpointTransactionAndFenceWrites()
    {
        var source = ReadRepository();
        Assert.Contains("run.lease_token = @leaseToken", source, StringComparison.Ordinal);
        Assert.Contains("run.fencing_version = @fencingVersion", source, StringComparison.Ordinal);
        Assert.Contains("run.lease_expires_at > clock_timestamp()", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("step.tenant_id = @tenantId", source, StringComparison.Ordinal);
        var checkpointMethod = source.Substring(source.IndexOf("TrySaveCheckpointAsync", StringComparison.Ordinal),
            source.IndexOf("TryMarkInterruptedNeedsReviewAsync", StringComparison.Ordinal) -
            source.IndexOf("TrySaveCheckpointAsync", StringComparison.Ordinal));
        Assert.Contains("client.Ado.BeginTran()", checkpointMethod, StringComparison.Ordinal);
        Assert.Contains("client.Ado.CommitTran()", checkpointMethod, StringComparison.Ordinal);
        Assert.Contains("client.Ado.RollbackTran()", checkpointMethod, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashData(bytes)", checkpointMethod, StringComparison.Ordinal);
        Assert.Contains("MaxCheckpointBytes = 1_048_576", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderCheckpointDto_ShouldPreserveReplayCompletionAndFailureMetadata()
    {
        var properties = typeof(XiHan.BasicApp.Crm.Domain.Prospecting.CrmProspectingProviderResultCheckpoint)
            .GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Records", properties);
        Assert.Contains("IsComplete", properties);
        Assert.Contains("FailureCount", properties);
    }

    [Fact]
    public void RunWorker_ShouldPersistVerifyAndReplayBoundedProviderCheckpoints()
    {
        var worker = ReadWorker();
        Assert.Contains("TrySaveCheckpointAsync", worker, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingProviderResultCheckpoint", worker, StringComparison.Ordinal);
        Assert.Contains("checkpointHash", worker, StringComparison.Ordinal);
        Assert.Contains("JsonSerializer.Deserialize<CrmProspectingProviderResultCheckpoint>", worker, StringComparison.Ordinal);
        Assert.Contains("executionStep.State is \"Checkpointed\" or \"Projecting\"", worker, StringComparison.Ordinal);
        Assert.Contains("\"Projecting\", \"Succeeded\"", worker, StringComparison.Ordinal);
        Assert.Contains("maxProviderCheckpointBytes = 1_048_576", worker, StringComparison.Ordinal);
        Assert.Contains("TryTransitionAsync(", worker, StringComparison.Ordinal);
        Assert.Contains("providerProjectionFailures", worker, StringComparison.Ordinal);
        Assert.Contains("MarkProviderStepFailedAsync", worker, StringComparison.Ordinal);
        Assert.Contains("MarkProviderStepSkippedAsync", worker, StringComparison.Ordinal);
        Assert.Contains("estimatedRawJsonBytes + replayRecordBytes + 1 > maxRawJsonBytes", worker, StringComparison.Ordinal);
    }

    private static string ReadWorker()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "src")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs"));
    }

    private static string ReadRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "src")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories",
            "CrmProspectingRunProviderExecutionRepository.cs"));
    }
}
