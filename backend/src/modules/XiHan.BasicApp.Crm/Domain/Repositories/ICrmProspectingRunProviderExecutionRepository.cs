using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped, Run-lease-fenced provider execution checkpoints.</summary>
public interface ICrmProspectingRunProviderExecutionRepository : ISaasRepository<SysCrmProspectingRunProviderExecution>
{
    Task<IReadOnlyList<SysCrmProspectingRunProviderExecution>> GetByRunAsync(long runId, CancellationToken cancellationToken = default);

    /// <summary>Idempotently materializes the immutable provider plan snapshot under the live Run lease.</summary>
    Task<IReadOnlyList<SysCrmProspectingRunProviderExecution>> EnsurePlanMaterializedAsync(long runId, Guid leaseToken, long fencingVersion, CancellationToken cancellationToken = default);

    /// <summary>Starts one queued provider step only while the exact Run lease is live.</summary>
    Task<bool> TryStartAsync(long executionId, long runId, Guid leaseToken, long fencingVersion, CancellationToken cancellationToken = default);

    /// <summary>Persists a bounded response checkpoint and transitions Started to Checkpointed in the caller's transaction.</summary>
    Task<bool> TrySaveCheckpointAsync(long executionId, long runId, Guid leaseToken, long fencingVersion,
        string checkpointJson, int rawRecordCount, CancellationToken cancellationToken = default);

    /// <summary>Conditionally advances a provider step under the exact live Run lease.</summary>
    Task<bool> TryTransitionAsync(long executionId, long runId, Guid leaseToken, long fencingVersion,
        string expectedState, string nextState, int? projectedRecordCount = null, int? failureCount = null,
        string? failureCode = null, string? safeFailureSummary = null, CancellationToken cancellationToken = default);
}
