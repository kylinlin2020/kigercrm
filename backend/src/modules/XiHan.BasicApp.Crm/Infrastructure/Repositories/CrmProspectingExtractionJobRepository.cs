using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>
/// PostgreSQL-backed extraction job store. Every operation is tenant-scoped and worker claims
/// use database time plus a monotonically increasing fencing version.
/// </summary>
public sealed class CrmProspectingExtractionJobRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingExtractionJob>(clientResolver), ICrmProspectingExtractionJobRepository
{
    private const int MaxMetadataLength = 4000;
    private const int MaxCursorLength = 2048;
    private const int MaxFailureSummaryLength = 1000;

    public async Task<SysCrmProspectingExtractionJob> CreateAsync(
        long runId,
        string providerCode,
        string requestFingerprint,
        string providerExtractionId,
        DateTimeOffset submittedAtUtc,
        DateTimeOffset nextPollAtUtc,
        DateTimeOffset? expiresAtUtc,
        string? safeMetadataJson,
        CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerExtractionId);
        providerCode = providerCode.Trim().ToLowerInvariant();
        if (providerCode.Length > 80) throw new ArgumentOutOfRangeException(nameof(providerCode));
        if (requestFingerprint.Length != 64 || !requestFingerprint.All(Uri.IsHexDigit))
            throw new ArgumentException("Request fingerprint must be a 64-character SHA-256 hex digest.", nameof(requestFingerprint));
        if (providerExtractionId.Length > 512) throw new ArgumentOutOfRangeException(nameof(providerExtractionId));
        if (expiresAtUtc is not null && expiresAtUtc <= submittedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        if (nextPollAtUtc < submittedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(nextPollAtUtc));
        if (safeMetadataJson is { Length: > MaxMetadataLength })
            throw new ArgumentOutOfRangeException(nameof(safeMetadataJson));
        cancellationToken.ThrowIfCancellationRequested();

        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for extraction job creation.");

        var existing = await CreateQueryable()
            .Where(row => row.TenantId == tenant.TenantId
                && row.RunId == runId
                && row.ProviderCode == providerCode
                && row.RequestFingerprint == requestFingerprint.ToUpperInvariant())
            .Take(2)
            .ToListAsync(cancellationToken);
        if (existing.Count > 1)
            throw new InvalidOperationException("The tenant contains duplicate extraction jobs for one Run/provider/fingerprint.");
        if (existing.Count == 1)
            return existing[0];

        var entity = new SysCrmProspectingExtractionJob
        {
            TenantId = tenant.TenantId,
            RunId = runId,
            ProviderCode = providerCode,
            RequestFingerprint = requestFingerprint.ToUpperInvariant(),
            ProviderExtractionId = providerExtractionId,
            Status = CrmProspectingExtractionStatus.Pending.ToString(),
            SubmittedAtUtc = submittedAtUtc,
            NextPollAtUtc = nextPollAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            SafeMetadataJson = safeMetadataJson,
            PollAttemptCount = 0,
            ResultRecordCount = 0,
            FencingVersion = 0
        };
        try
        {
            await AddAsync(entity);
            return entity;
        }
        catch (SqlSugarException)
        {
            // A competing worker may have inserted the same tenant/run/provider/fingerprint.
            // Re-read only that exact idempotency key; preserve the original error otherwise.
            var raced = await CreateQueryable()
                .Where(row => row.TenantId == tenant.TenantId
                    && row.RunId == runId
                    && row.ProviderCode == providerCode
                    && row.RequestFingerprint == requestFingerprint.ToUpperInvariant())
                .Take(2)
                .ToListAsync(cancellationToken);
            if (raced.Count == 1)
                return raced[0];
            throw;
        }
    }

    public async Task<SysCrmProspectingExtractionJob?> GetByIdAsync(
        long jobId,
        CancellationToken cancellationToken = default)
    {
        if (jobId <= 0) throw new ArgumentOutOfRangeException(nameof(jobId));
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for extraction job reads.");
        var rows = await CreateQueryable()
            .Where(row => row.TenantId == tenant.TenantId && row.BasicId == jobId)
            .Take(1)
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<IReadOnlyList<SysCrmProspectingExtractionJob>> ClaimDueAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (leaseOwner.Length > 160) throw new ArgumentOutOfRangeException(nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        cancellationToken.ThrowIfCancellationRequested();

        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("Durable extraction job claims require PostgreSQL.");
        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for extraction job claims.");
        var durationMilliseconds = checked((long)Math.Ceiling(leaseDuration.TotalMilliseconds));

        const string sql = """
            WITH due_jobs AS (
                SELECT job.basic_id
                FROM crm_prospecting_extraction_job AS job
                WHERE job.tenant_id = @tenantId
                  AND job.status IN (@pendingStatus, @runningStatus, @fetchingStatus)
                  AND job.next_poll_at_utc <= clock_timestamp()
                  AND (job.expires_at_utc IS NULL OR job.expires_at_utc > clock_timestamp())
                  AND (job.lease_expires_at_utc IS NULL OR job.lease_expires_at_utc <= clock_timestamp())
                ORDER BY job.next_poll_at_utc, job.basic_id
                FOR UPDATE SKIP LOCKED
                LIMIT @batchSize
            )
            UPDATE crm_prospecting_extraction_job AS job
            SET lease_owner = @leaseOwner,
                lease_token = md5(job.basic_id::text || clock_timestamp()::text || random()::text)::uuid,
                lease_expires_at_utc = clock_timestamp() + (@leaseDurationMilliseconds * INTERVAL '1 millisecond'),
                fencing_version = job.fencing_version + 1
            FROM due_jobs
            WHERE job.tenant_id = @tenantId
              AND job.basic_id = due_jobs.basic_id
            RETURNING job.*;
            """;

        var rows = await client.Ado.SqlQueryAsync<SysCrmProspectingExtractionJob>(
            sql,
            new SugarParameter("@tenantId", tenant.TenantId),
            new SugarParameter("@pendingStatus", CrmProspectingExtractionStatus.Pending.ToString()),
            new SugarParameter("@runningStatus", CrmProspectingExtractionStatus.Running.ToString()),
            new SugarParameter("@fetchingStatus", CrmProspectingExtractionStatus.FetchingResults.ToString()),
            new SugarParameter("@leaseOwner", leaseOwner),
            new SugarParameter("@leaseDurationMilliseconds", durationMilliseconds),
            new SugarParameter("@batchSize", batchSize));
        return rows;
    }

    public async Task<bool> TryTransitionAsync(
        long jobId,
        CrmProspectingExtractionStatus expectedStatus,
        CrmProspectingExtractionStatus nextStatus,
        Guid leaseToken,
        long fencingVersion,
        DateTimeOffset? nextPollAtUtc = null,
        DateTimeOffset? lastPolledAtUtc = null,
        string? failureCode = null,
        string? safeFailureSummary = null,
        CancellationToken cancellationToken = default)
    {
        if (jobId <= 0) throw new ArgumentOutOfRangeException(nameof(jobId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
        if (!CrmProspectingExtractionStateMachine.CanTransition(expectedStatus, nextStatus))
            throw new InvalidOperationException("The requested extraction lifecycle transition is not allowed.");
        if (safeFailureSummary is { Length: > MaxFailureSummaryLength })
            safeFailureSummary = safeFailureSummary[..MaxFailureSummaryLength];
        if (failureCode is { Length: > 80 })
            failureCode = failureCode[..80];
        cancellationToken.ThrowIfCancellationRequested();

        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for extraction job transitions.");
        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("Extraction job transitions require PostgreSQL.");
        var nextPoll = nextPollAtUtc ?? DateTimeOffset.UtcNow;
        var safeSummary = string.IsNullOrWhiteSpace(safeFailureSummary)
            ? null
            : "Provider extraction failed; details withheld.";

        const string sql = """
            UPDATE crm_prospecting_extraction_job
            SET status = @nextStatus,
                next_poll_at_utc = @nextPollAtUtc,
                last_polled_at_utc = COALESCE(@lastPolledAtUtc, last_polled_at_utc),
                poll_attempt_count = poll_attempt_count + CASE WHEN @countPoll THEN 1 ELSE 0 END,
                failure_code = @failureCode,
                failure_summary = @failureSummary,
                lease_owner = NULL,
                lease_token = NULL,
                lease_expires_at_utc = NULL
            WHERE tenant_id = @tenantId
              AND basic_id = @jobId
              AND status = @expectedStatus
              AND lease_token = @leaseToken
              AND fencing_version = @fencingVersion;
            """;
        var affected = await client.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@nextStatus", nextStatus.ToString()),
            new SugarParameter("@nextPollAtUtc", nextPoll),
            new SugarParameter("@lastPolledAtUtc", lastPolledAtUtc),
            new SugarParameter("@countPoll", lastPolledAtUtc is not null),
            new SugarParameter("@failureCode", failureCode),
            new SugarParameter("@failureSummary", safeSummary),
            new SugarParameter("@tenantId", tenant.TenantId),
            new SugarParameter("@jobId", jobId),
            new SugarParameter("@expectedStatus", expectedStatus.ToString()),
            new SugarParameter("@leaseToken", leaseToken),
            new SugarParameter("@fencingVersion", fencingVersion));
        return affected == 1;
    }

    public async Task<bool> TryAdvanceCursorAsync(
        long jobId,
        Guid leaseToken,
        long fencingVersion,
        string? expectedCursor,
        string? nextCursor,
        int persistedRecordCount,
        CancellationToken cancellationToken = default)
    {
        if (jobId <= 0) throw new ArgumentOutOfRangeException(nameof(jobId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
        if (expectedCursor is { Length: > MaxCursorLength } || nextCursor is { Length: > MaxCursorLength })
            throw new ArgumentOutOfRangeException(nameof(nextCursor));
        if (persistedRecordCount < 0) throw new ArgumentOutOfRangeException(nameof(persistedRecordCount));
        cancellationToken.ThrowIfCancellationRequested();

        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("Extraction cursor advancement requires PostgreSQL.");
        if (client.Ado.Transaction is null)
            throw new InvalidOperationException("Cursor advancement must share a transaction with result/evidence persistence.");
        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for extraction cursor advancement.");

        const string sql = """
            UPDATE crm_prospecting_extraction_job
            SET result_cursor = @nextCursor,
                result_record_count = result_record_count + @persistedRecordCount
            WHERE tenant_id = @tenantId
              AND basic_id = @jobId
              AND status = @fetchingStatus
              AND lease_token = @leaseToken
              AND fencing_version = @fencingVersion
              AND result_cursor IS NOT DISTINCT FROM @expectedCursor;
            """;
        var affected = await client.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@nextCursor", nextCursor),
            new SugarParameter("@persistedRecordCount", persistedRecordCount),
            new SugarParameter("@tenantId", tenant.TenantId),
            new SugarParameter("@jobId", jobId),
            new SugarParameter("@fetchingStatus", CrmProspectingExtractionStatus.FetchingResults.ToString()),
            new SugarParameter("@leaseToken", leaseToken),
            new SugarParameter("@fencingVersion", fencingVersion),
            new SugarParameter("@expectedCursor", expectedCursor));
        return affected == 1;
    }
}
