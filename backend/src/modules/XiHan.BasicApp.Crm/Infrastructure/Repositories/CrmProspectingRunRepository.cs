using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingRunRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingRun>(clientResolver), ICrmProspectingRunRepository
{
    private const int MaxFailureSummaryLength = 2000;

    private static string? BoundFailureSummary(string? summary)
        => summary is { Length: > MaxFailureSummaryLength } ? summary[..MaxFailureSummaryLength] : summary;

    public async Task<SysCrmProspectingRun?> GetByRequestKeyAsync(string requestKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await CreateQueryable()
            .Where(run => run.RequestKey == requestKey)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (rows.Count > 1)
            throw new InvalidOperationException("The tenant contains multiple Runs for one idempotency key; the unique-key invariant is broken.");
        return rows.FirstOrDefault();
    }

    public async Task<SysCrmProspectingRun?> ClaimQueuedAsync(
        long runId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Conditional update is the concurrency boundary: only one worker can move Queued -> Running.
        var claimed = await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = CrmProspectingRunStatus.Running,
                StartedAt = startedAt,
                CompletedAt = null,
                FailureSummary = null
            },
            run => run.BasicId == runId && run.Status == CrmProspectingRunStatus.Queued,
            cancellationToken);

        if (!claimed)
        {
            return null;
        }

        var rows = await CreateQueryable()
            .Where(run => run.BasicId == runId && run.Status == CrmProspectingRunStatus.Running)
            .Take(1)
            .ToListAsync(cancellationToken);

        return rows.FirstOrDefault();
    }

    public async Task<bool> RenewLeaseAsync(
        long runId,
        Guid leaseToken,
        long fencingVersion,
        DateTimeOffset heartbeatAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be greater than zero and at most one hour.");
        cancellationToken.ThrowIfCancellationRequested();

        return await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                LeaseHeartbeatAt = heartbeatAt,
                LeaseExpiresAt = heartbeatAt.Add(leaseDuration)
            },
            run => run.BasicId == runId &&
                run.Status == CrmProspectingRunStatus.Running &&
                run.LeaseToken == leaseToken &&
                run.FencingVersion == fencingVersion &&
                run.LeaseExpiresAt != null &&
                run.LeaseExpiresAt > heartbeatAt,
            cancellationToken);
    }

    public async Task<bool> CompleteFencedAsync(
        long runId,
        Guid leaseToken,
        long fencingVersion,
        DateTimeOffset now,
        string rawResultsJson,
        int rawRecordCount,
        int failureCount,
        string? failureSummary,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default,
        int candidateCount = 0)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
        ArgumentNullException.ThrowIfNull(rawResultsJson);
        if (rawRecordCount < 0 || candidateCount < 0 || failureCount < 0)
            throw new ArgumentOutOfRangeException(nameof(rawRecordCount));
        cancellationToken.ThrowIfCancellationRequested();

        var status = failureCount == 0
            ? CrmProspectingRunStatus.Succeeded
            : rawRecordCount > 0
                ? CrmProspectingRunStatus.PartiallySucceeded
                : CrmProspectingRunStatus.Failed;

        return await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = status,
                RawResultsJson = rawResultsJson,
                RawRecordCount = rawRecordCount,
                CandidateCount = candidateCount,
                FailureCount = failureCount,
                FailureSummary = BoundFailureSummary(failureSummary),
                CompletedAt = completedAt,
                LeaseOwner = null,
                LeaseToken = null,
                LeaseExpiresAt = null,
                LeaseHeartbeatAt = null
            },
            run => run.BasicId == runId &&
                run.Status == CrmProspectingRunStatus.Running &&
                run.LeaseToken == leaseToken &&
                run.FencingVersion == fencingVersion &&
                run.LeaseExpiresAt != null &&
                run.LeaseExpiresAt > now,
            cancellationToken);
    }

    public async Task<bool> CancelFencedAsync(
        long runId,
        Guid leaseToken,
        long fencingVersion,
        DateTimeOffset now,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
        cancellationToken.ThrowIfCancellationRequested();

        return await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = CrmProspectingRunStatus.Cancelled,
                CompletedAt = completedAt,
                LeaseOwner = null,
                LeaseToken = null,
                LeaseExpiresAt = null,
                LeaseHeartbeatAt = null
            },
            run => run.BasicId == runId &&
                run.Status == CrmProspectingRunStatus.CancelRequested &&
                run.LeaseToken == leaseToken &&
                run.FencingVersion == fencingVersion &&
                run.LeaseExpiresAt != null &&
                run.LeaseExpiresAt > now,
            cancellationToken);
    }

    public async Task<bool> FailFencedAsync(
        long runId,
        Guid leaseToken,
        long fencingVersion,
        DateTimeOffset now,
        string failureSummary,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(failureSummary);
        cancellationToken.ThrowIfCancellationRequested();

        return await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = CrmProspectingRunStatus.Failed,
                FailureCount = 1,
                FailureSummary = BoundFailureSummary(failureSummary),
                CompletedAt = completedAt,
                LeaseOwner = null,
                LeaseToken = null,
                LeaseExpiresAt = null,
                LeaseHeartbeatAt = null
            },
            run => run.BasicId == runId &&
                run.Status == CrmProspectingRunStatus.Running &&
                run.LeaseToken == leaseToken &&
                run.FencingVersion == fencingVersion &&
                run.LeaseExpiresAt != null &&
                run.LeaseExpiresAt > now,
            cancellationToken);
    }

    public async Task<IReadOnlyList<SysCrmProspectingRun>> GetLatestByCampaignAsync(long campaignId, int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var take = Math.Clamp(limit, 1, 500);
        return await CreateQueryable()
            .Where(run => run.CampaignId == campaignId)
            .OrderByDescending(run => run.CreatedTime)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<long>> GetQueuedIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await CreateQueryable()
            .Where(run => run.Status == CrmProspectingRunStatus.Queued)
            .OrderBy(run => run.CreatedTime)
            .Select(run => run.BasicId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> RecoverInterruptedAsync(
        DateTimeOffset recoveredAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A crashed worker can safely be retried from the immutable strategy snapshot.
        var requeued = await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = CrmProspectingRunStatus.Queued,
                StartedAt = null
            },
            run => run.Status == CrmProspectingRunStatus.Running,
            cancellationToken);

        // Cancellation intent survives process death and must not be converted back to executable work.
        var cancelled = await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = CrmProspectingRunStatus.Cancelled,
                CompletedAt = recoveredAt
            },
            run => run.Status == CrmProspectingRunStatus.CancelRequested,
            cancellationToken);

        return requeued || cancelled;
    }

    public async Task<bool> CompleteAsync(
        long runId,
        string rawResultsJson,
        int rawRecordCount,
        int failureCount,
        string? failureSummary,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default,
        int candidateCount = 0)
    {
        failureSummary = BoundFailureSummary(failureSummary);
        var status = failureCount == 0
            ? CrmProspectingRunStatus.Succeeded
            : rawRecordCount > 0
                ? CrmProspectingRunStatus.PartiallySucceeded
                : CrmProspectingRunStatus.Failed;

        var completed = await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = status,
                RawResultsJson = rawResultsJson,
                RawRecordCount = rawRecordCount,
                CandidateCount = candidateCount,
                FailureCount = failureCount,
                FailureSummary = failureSummary,
                CompletedAt = completedAt
            },
            run => run.BasicId == runId && run.Status == CrmProspectingRunStatus.Running,
            cancellationToken);
        if (completed) return true;

        // Cancellation wins if it changed Running to CancelRequested before completion.
        return await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = CrmProspectingRunStatus.Cancelled,
                RawResultsJson = rawResultsJson,
                RawRecordCount = rawRecordCount,
                CandidateCount = candidateCount,
                FailureCount = failureCount,
                FailureSummary = failureSummary,
                CompletedAt = completedAt
            },
            run => run.BasicId == runId && run.Status == CrmProspectingRunStatus.CancelRequested,
            cancellationToken);
    }

    public async Task<bool> FailAsync(
        long runId,
        string failureSummary,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        failureSummary = BoundFailureSummary(failureSummary) ?? string.Empty;
        var failed = await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = CrmProspectingRunStatus.Failed,
                FailureCount = 1,
                FailureSummary = failureSummary,
                CompletedAt = completedAt
            },
            run => run.BasicId == runId && run.Status == CrmProspectingRunStatus.Running,
            cancellationToken);
        if (failed) return true;

        return await UpdateAsync(
            run => new SysCrmProspectingRun
            {
                Status = CrmProspectingRunStatus.Cancelled,
                CompletedAt = completedAt
            },
            run => run.BasicId == runId && run.Status == CrmProspectingRunStatus.CancelRequested,
            cancellationToken);
    }
}
