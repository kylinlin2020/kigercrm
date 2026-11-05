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
    private const int MaxCursorLength = 2048;
    private const int MaxFailureSummaryLength = 1000;

    public async Task<SysCrmProspectingExtractionJob> CreateSubmissionIntentAsync(
        long runId, string providerCode, string requestFingerprint, DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        providerCode = providerCode.Trim().ToLowerInvariant();
        if (providerCode.Length > 80) throw new ArgumentOutOfRangeException(nameof(providerCode));
        if (requestFingerprint.Length != 64 || !requestFingerprint.All(Uri.IsHexDigit))
            throw new ArgumentException("Request fingerprint must be a 64-character SHA-256 hex digest.", nameof(requestFingerprint));
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for extraction intent creation.");
        var fingerprint = requestFingerprint.ToUpperInvariant();
        var existing = await CreateQueryable()
            .Where(row => row.TenantId == tenant.TenantId && row.RunId == runId
                && row.ProviderCode == providerCode && row.RequestFingerprint == fingerprint)
            .Take(2).ToListAsync(cancellationToken);
        if (existing.Count > 1)
            throw new InvalidOperationException("The tenant contains duplicate extraction intents for one Run/provider/fingerprint.");
        if (existing.Count == 1) return existing[0];

        var entity = new SysCrmProspectingExtractionJob
        {
            TenantId = tenant.TenantId, RunId = runId, ProviderCode = providerCode,
            RequestFingerprint = fingerprint, SubmissionIdempotencyKey = Guid.NewGuid(),
            ProviderExtractionId = null, Status = CrmProspectingExtractionStatus.Submitting.ToString(),
            SubmittedAtUtc = null, NextPollAtUtc = nextAttemptAtUtc, PollAttemptCount = 0,
            ResultRecordCount = 0, FencingVersion = 0
        };
        try { await AddAsync(entity); return entity; }
        catch (SqlSugarException)
        {
            var raced = await CreateQueryable()
                .Where(row => row.TenantId == tenant.TenantId && row.RunId == runId
                    && row.ProviderCode == providerCode && row.RequestFingerprint == fingerprint)
                .Take(2).ToListAsync(cancellationToken);
            if (raced.Count == 1) return raced[0];
            throw;
        }
    }

    public async Task<bool> TryAcknowledgeSubmissionAsync(
        long jobId, Guid leaseToken, long fencingVersion, string providerExtractionId,
        DateTimeOffset submittedAtUtc, DateTimeOffset nextPollAtUtc, DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (jobId <= 0) throw new ArgumentOutOfRangeException(nameof(jobId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerExtractionId);
        if (providerExtractionId.Length > CrmProspectingExtractionSubmissionValidator.MaxProviderExtractionIdLength)
            throw new ArgumentOutOfRangeException(nameof(providerExtractionId));
        if (expiresAtUtc is not null && expiresAtUtc <= submittedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        if (nextPollAtUtc < submittedAtUtc) throw new ArgumentOutOfRangeException(nameof(nextPollAtUtc));
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for extraction acknowledgement.");

        // Validate at the persistence boundary too: callers cannot accidentally bypass the
        // adapter-level validation and persist a mismatched/unsafe remote acknowledgement.
        var intent = await CreateQueryable()
            .Where(row => row.TenantId == tenant.TenantId && row.BasicId == jobId)
            .Take(1).ToListAsync(cancellationToken);
        if (intent.Count != 1)
            return false;
        var submission = new CrmProspectingExtractionSubmission(
            new CrmProspectingExtractionReference(intent[0].ProviderCode, providerExtractionId),
            submittedAtUtc, expiresAtUtc, nextPollAtUtc - submittedAtUtc, null);
        if (!CrmProspectingExtractionSubmissionValidator.TryValidate(
                submission, intent[0].ProviderCode, out var validationFailure))
            throw new ArgumentException(
                $"Provider extraction acknowledgement failed validation: {validationFailure}.",
                nameof(providerExtractionId));

        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("Extraction acknowledgement requires PostgreSQL.");

        const string sql = """
            UPDATE crm_prospecting_extraction_job
            SET provider_extraction_id = @providerExtractionId,
                status = @pendingStatus, submitted_at_utc = @submittedAtUtc,
                next_poll_at_utc = @nextPollAtUtc, expires_at_utc = @expiresAtUtc,
                lease_owner = NULL, lease_token = NULL, lease_expires_at_utc = NULL
            WHERE tenant_id = @tenantId AND basic_id = @jobId
              AND status = @submittingStatus AND provider_extraction_id IS NULL
              AND lease_token = @leaseToken AND fencing_version = @fencingVersion
              AND lease_expires_at_utc > clock_timestamp();
            """;
        var affected = await client.Ado.ExecuteCommandAsync(sql,
            new SugarParameter("@providerExtractionId", providerExtractionId),
            new SugarParameter("@pendingStatus", CrmProspectingExtractionStatus.Pending.ToString()),
            new SugarParameter("@submittedAtUtc", submittedAtUtc),
            new SugarParameter("@nextPollAtUtc", nextPollAtUtc),
            new SugarParameter("@expiresAtUtc", expiresAtUtc),
            new SugarParameter("@tenantId", tenant.TenantId),
            new SugarParameter("@jobId", jobId),
            new SugarParameter("@submittingStatus", CrmProspectingExtractionStatus.Submitting.ToString()),
            new SugarParameter("@leaseToken", leaseToken),
            new SugarParameter("@fencingVersion", fencingVersion));
        return affected == 1;
    }

    [Obsolete("Direct acknowledged-job creation bypasses the durable Submitting intent and is disabled. Use CreateSubmissionIntentAsync, claim the intent, submit with its idempotency key, then call TryAcknowledgeSubmissionAsync.")]
    public Task<SysCrmProspectingExtractionJob> CreateAsync(
        long runId,
        string providerCode,
        string requestFingerprint,
        string providerExtractionId,
        DateTimeOffset submittedAtUtc,
        DateTimeOffset nextPollAtUtc,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Direct extraction creation is disabled because it bypasses the durable pre-submit intent. Use CreateSubmissionIntentAsync and TryAcknowledgeSubmissionAsync.");

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
                  AND (
                      job.status IN (@submittingStatus, @pendingStatus, @runningStatus, @succeededStatus, @fetchingStatus)
                  )
                  AND job.next_poll_at_utc <= clock_timestamp()
                  AND (job.expires_at_utc IS NULL OR job.expires_at_utc > clock_timestamp())
                  AND (job.lease_expires_at_utc IS NULL OR job.lease_expires_at_utc <= clock_timestamp())
                ORDER BY job.next_poll_at_utc, job.basic_id
                LIMIT @batchSize
                FOR UPDATE SKIP LOCKED
            )
            UPDATE crm_prospecting_extraction_job AS job
            SET lease_owner = @leaseOwner,
                lease_token = gen_random_uuid(),
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
            new SugarParameter("@submittingStatus", CrmProspectingExtractionStatus.Submitting.ToString()),
            new SugarParameter("@pendingStatus", CrmProspectingExtractionStatus.Pending.ToString()),
            new SugarParameter("@runningStatus", CrmProspectingExtractionStatus.Running.ToString()),
            new SugarParameter("@succeededStatus", CrmProspectingExtractionStatus.Succeeded.ToString()),
            new SugarParameter("@fetchingStatus", CrmProspectingExtractionStatus.FetchingResults.ToString()),
            new SugarParameter("@leaseOwner", leaseOwner),
            new SugarParameter("@leaseDurationMilliseconds", durationMilliseconds),
            new SugarParameter("@batchSize", batchSize));
        return rows;
    }

    public async Task<bool> TryRenewLeaseAsync(
        long jobId,
        Guid leaseToken,
        long fencingVersion,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (jobId <= 0) throw new ArgumentOutOfRangeException(nameof(jobId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        cancellationToken.ThrowIfCancellationRequested();

        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for extraction lease renewal.");
        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("Extraction lease renewal requires PostgreSQL.");
        var durationMilliseconds = checked((long)Math.Ceiling(leaseDuration.TotalMilliseconds));

        const string sql = """
            UPDATE crm_prospecting_extraction_job
            SET lease_expires_at_utc = clock_timestamp() + (@leaseDurationMilliseconds * INTERVAL '1 millisecond')
            WHERE tenant_id = @tenantId
              AND basic_id = @jobId
              AND lease_token = @leaseToken
              AND fencing_version = @fencingVersion
              AND lease_expires_at_utc > clock_timestamp()
              AND status IN (@submittingStatus, @pendingStatus, @runningStatus, @succeededStatus, @fetchingStatus);
            """;
        var affected = await client.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@leaseDurationMilliseconds", durationMilliseconds),
            new SugarParameter("@tenantId", tenant.TenantId),
            new SugarParameter("@jobId", jobId),
            new SugarParameter("@leaseToken", leaseToken),
            new SugarParameter("@fencingVersion", fencingVersion),
            new SugarParameter("@submittingStatus", CrmProspectingExtractionStatus.Submitting.ToString()),
            new SugarParameter("@pendingStatus", CrmProspectingExtractionStatus.Pending.ToString()),
            new SugarParameter("@runningStatus", CrmProspectingExtractionStatus.Running.ToString()),
            new SugarParameter("@succeededStatus", CrmProspectingExtractionStatus.Succeeded.ToString()),
            new SugarParameter("@fetchingStatus", CrmProspectingExtractionStatus.FetchingResults.ToString()));
        return affected == 1;
    }

    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("Extraction expiry recovery requires PostgreSQL.");
        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for extraction expiry recovery.");

        const string sql = """
            UPDATE crm_prospecting_extraction_job
            SET status = 'Expired',
                failure_code = 'provider_extraction_expired',
                failure_summary = 'Provider extraction expired before complete result retrieval.',
                lease_owner = NULL,
                lease_token = NULL,
                lease_expires_at_utc = NULL
            WHERE tenant_id = @tenantId
              AND status IN (@pendingStatus, @runningStatus, @succeededStatus, @fetchingStatus)
              AND expires_at_utc IS NOT NULL
              AND expires_at_utc <= clock_timestamp();
            """;
        return await client.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@tenantId", tenant.TenantId),
            new SugarParameter("@pendingStatus", CrmProspectingExtractionStatus.Pending.ToString()),
            new SugarParameter("@runningStatus", CrmProspectingExtractionStatus.Running.ToString()),
            new SugarParameter("@succeededStatus", CrmProspectingExtractionStatus.Succeeded.ToString()),
            new SugarParameter("@fetchingStatus", CrmProspectingExtractionStatus.FetchingResults.ToString()));
    }

    public async Task<bool> TryCompleteResultsAsync(
        long jobId,
        Guid leaseToken,
        long fencingVersion,
        bool isLastPage,
        bool isComplete,
        int failureCount,
        CancellationToken cancellationToken = default,
        int persistedRecordCount = 0,
        string? expectedCursor = null)
    {
        if (jobId <= 0) throw new ArgumentOutOfRangeException(nameof(jobId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
        if (persistedRecordCount is < 0 or > CrmProspectingExtractionPageValidator.MaxRecordsPerPage)
            throw new ArgumentOutOfRangeException(nameof(persistedRecordCount));
        if (expectedCursor is { Length: > MaxCursorLength } || (expectedCursor is not null && expectedCursor.Any(char.IsControl)))
            throw new ArgumentOutOfRangeException(nameof(expectedCursor));
        if (!CrmProspectingExtractionStateMachine.CanCompleteResults(
                CrmProspectingExtractionStatus.FetchingResults, isLastPage, isComplete, failureCount))
            throw new InvalidOperationException(
                "Extraction results can complete only after a complete final page with zero record failures.");
        cancellationToken.ThrowIfCancellationRequested();

        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for extraction completion.");
        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("Extraction completion requires PostgreSQL.");
        if (client.Ado.Transaction is null)
            throw new InvalidOperationException("Final-page completion must share a transaction with result/evidence persistence.");

        const string sql = """
            UPDATE crm_prospecting_extraction_job
            SET status = @completedStatus,
                result_record_count = result_record_count + @persistedRecordCount,
                lease_owner = NULL,
                lease_token = NULL,
                lease_expires_at_utc = NULL,
                failure_code = NULL,
                failure_summary = NULL
            WHERE tenant_id = @tenantId
              AND basic_id = @jobId
              AND status = @fetchingStatus
              AND lease_token = @leaseToken
              AND fencing_version = @fencingVersion
              AND result_cursor IS NOT DISTINCT FROM @expectedCursor
              AND lease_expires_at_utc > clock_timestamp();
            """;
        var affected = await client.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@completedStatus", CrmProspectingExtractionStatus.Completed.ToString()),
            new SugarParameter("@persistedRecordCount", persistedRecordCount),
            new SugarParameter("@tenantId", tenant.TenantId),
            new SugarParameter("@jobId", jobId),
            new SugarParameter("@fetchingStatus", CrmProspectingExtractionStatus.FetchingResults.ToString()),
            new SugarParameter("@leaseToken", leaseToken),
            new SugarParameter("@fencingVersion", fencingVersion),
            new SugarParameter("@expectedCursor", expectedCursor));
        return affected == 1;
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
        if (nextStatus == CrmProspectingExtractionStatus.Completed)
            throw new InvalidOperationException(
                "Use TryCompleteResultsAsync with final-page validation to complete extraction results.");
        if (expectedStatus == CrmProspectingExtractionStatus.Submitting
            && nextStatus == CrmProspectingExtractionStatus.Pending)
            throw new InvalidOperationException(
                "Use TryAcknowledgeSubmissionAsync to persist the provider extraction identity before entering Pending.");
        if (!CrmProspectingExtractionStateMachine.CanTransition(expectedStatus, nextStatus))
            throw new InvalidOperationException("The requested extraction lifecycle transition is not allowed.");
        if (safeFailureSummary is { Length: > MaxFailureSummaryLength })
            safeFailureSummary = safeFailureSummary[..MaxFailureSummaryLength];
        failureCode = failureCode is not null && failureCode.Length <= 80
            && failureCode.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')
                ? failureCode
                : (failureCode is null ? null : "provider_error");
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
              AND (@requiresProviderExtractionId = FALSE OR provider_extraction_id IS NOT NULL)
              AND lease_token = @leaseToken
              AND fencing_version = @fencingVersion
              AND lease_expires_at_utc > clock_timestamp();
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
            new SugarParameter("@requiresProviderExtractionId",
                CrmProspectingExtractionStateMachine.RequiresProviderExtractionId(nextStatus)),
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
        if (nextCursor is not null
            && (string.IsNullOrWhiteSpace(nextCursor)
                || string.Equals(expectedCursor, nextCursor, StringComparison.Ordinal)))
            throw new ArgumentException("A non-final result cursor must be non-empty and advance beyond the expected cursor.", nameof(nextCursor));
        if (persistedRecordCount is < 0 or > CrmProspectingExtractionPageValidator.MaxRecordsPerPage)
            throw new ArgumentOutOfRangeException(nameof(persistedRecordCount));
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
              AND result_cursor IS NOT DISTINCT FROM @expectedCursor
              AND lease_expires_at_utc > clock_timestamp();
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
