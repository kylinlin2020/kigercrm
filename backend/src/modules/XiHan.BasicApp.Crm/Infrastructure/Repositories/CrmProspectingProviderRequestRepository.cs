using System.Text;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.MultiTenancy;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using SqlSugar;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingProviderRequestRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingProviderRequest>(clientResolver), ICrmProspectingProviderRequestRepository
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.Ordinal)
    {
        "Succeeded", "Failed", "RateLimited", "Cancelled"
    };

    public async Task<SysCrmProspectingProviderRequest> CreateStartedAsync(
        long runId, string providerCode, string requestKey, DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        providerCode = providerCode.Trim().ToLowerInvariant();
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        if (providerCode.Length > 80) throw new ArgumentOutOfRangeException(nameof(providerCode));
        if (requestKey.Length != 64 || !requestKey.All(Uri.IsHexDigit))
            throw new ArgumentException("Request key must be a 64-character SHA-256 hex digest.", nameof(requestKey));
        cancellationToken.ThrowIfCancellationRequested();

        // Attempt allocation is optimistic: the unique index arbitrates concurrent workers.
        // If another worker inserts the same next number between our read and insert, re-read
        // the maximum and retry. Other persistence failures are rethrown unchanged.
        for (var collision = 0; collision < 5; collision++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = await CreateQueryable()
                .Where(row => row.RunId == runId && row.ProviderCode == providerCode)
                .OrderByDescending(row => row.AttemptNo)
                .Take(1)
                .ToListAsync(cancellationToken);
            var attemptNo = checked((previous.FirstOrDefault()?.AttemptNo ?? 0) + 1);

            var entity = new SysCrmProspectingProviderRequest
            {
                RunId = runId,
                ProviderCode = providerCode,
                AttemptNo = attemptNo,
                RequestKey = requestKey.ToUpperInvariant(),
                State = "Started",
                StartedAt = startedAt,
                ResultCount = 0,
                CacheHit = false
            };

            try
            {
                await AddAsync(entity);
                return entity;
            }
            catch (SqlSugarException) when (collision < 4)
            {
                // Only retry when a competing insert demonstrably advanced this exact
                // tenant/run/provider sequence. Otherwise preserve the original exception.
                var latest = await CreateQueryable()
                    .Where(row => row.RunId == runId && row.ProviderCode == providerCode)
                    .OrderByDescending(row => row.AttemptNo)
                    .Take(1)
                    .ToListAsync(cancellationToken);
                if ((latest.FirstOrDefault()?.AttemptNo ?? 0) < attemptNo)
                    throw;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique provider request attempt after concurrent retries.");
    }

    public async Task<bool> TryFinalizeAsync(
        long requestId, string terminalState, DateTimeOffset completedAt, long durationMs,
        int resultCount = 0, int? httpStatus = null, string? failureCode = null,
        string? failureSummary = null, DateTimeOffset? retryAfterAt = null,
        string? responseHash = null, bool cacheHit = false,
        CancellationToken cancellationToken = default)
    {
        if (requestId <= 0) throw new ArgumentOutOfRangeException(nameof(requestId));
        if (!TerminalStates.Contains(terminalState))
            throw new ArgumentException("A provider request can only transition to a terminal state.", nameof(terminalState));
        if (durationMs < 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
        if (resultCount < 0) throw new ArgumentOutOfRangeException(nameof(resultCount));
        if (httpStatus is < 100 or > 599) throw new ArgumentOutOfRangeException(nameof(httpStatus));
        if (responseHash is not null && (responseHash.Length != 64 || !responseHash.All(Uri.IsHexDigit)))
            throw new ArgumentException("Response hash must be a 64-character SHA-256 hex digest.", nameof(responseHash));
        cancellationToken.ThrowIfCancellationRequested();

        return await UpdateAsync(
            _ => new SysCrmProspectingProviderRequest
            {
                State = terminalState,
                CompletedAt = completedAt,
                DurationMs = durationMs,
                ResultCount = resultCount,
                HttpStatus = httpStatus,
                FailureCode = Bound(failureCode, 80),
                FailureSummary = SanitizeSummary(failureSummary),
                RetryAfterAt = retryAfterAt,
                ResponseHash = responseHash?.ToUpperInvariant(),
                CacheHit = cacheHit
            },
            row => row.BasicId == requestId && row.State == "Started",
            cancellationToken);
    }


    public async Task<bool> TryFinalizeSuccessWithCoverageAsync(
        long requestId, long campaignId, string coverageScopeHash,
        DateTimeOffset completedAt, long durationMs, int resultCount, string responseHash,
        CancellationToken cancellationToken = default)
    {
        if (requestId <= 0) throw new ArgumentOutOfRangeException(nameof(requestId));
        if (campaignId <= 0) throw new ArgumentOutOfRangeException(nameof(campaignId));
        if (coverageScopeHash.Length != 64 || !coverageScopeHash.All(Uri.IsHexDigit))
            throw new ArgumentException("Coverage scope must be a SHA-256 hex digest.", nameof(coverageScopeHash));
        if (responseHash.Length != 64 || !responseHash.All(Uri.IsHexDigit))
            throw new ArgumentException("Response hash must be a SHA-256 hex digest.", nameof(responseHash));
        if (durationMs < 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
        if (resultCount < 0) throw new ArgumentOutOfRangeException(nameof(resultCount));
        cancellationToken.ThrowIfCancellationRequested();

        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("Atomic provider coverage advancement requires PostgreSQL.");
        var currentTenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for provider coverage advancement.");

        // One PostgreSQL statement is the transaction boundary: if the coverage upsert fails,
        // the request-ledger finalization rolls back with it. A missing/mismatched Run or an
        // already-finalized request produces zero rows and cannot advance coverage.
        const string sql = """
            WITH finalized_request AS (
                UPDATE crm_prospecting_provider_request AS request
                SET state = 'Succeeded',
                    completed_at = @completedAt,
                    duration_ms = @durationMs,
                    result_count = @resultCount,
                    http_status = NULL,
                    failure_code = NULL,
                    failure_summary = NULL,
                    retry_after_at = NULL,
                    response_hash = @responseHash,
                    cache_hit = false
                WHERE request.tenant_id = @tenantId
                  AND request.basic_id = @requestId
                  AND request.state = 'Started'
                  AND EXISTS (
                      SELECT 1
                      FROM crm_prospecting_run AS run
                      WHERE run.tenant_id = request.tenant_id
                        AND run.basic_id = request.run_id
                        AND run.campaign_id = @campaignId
                  )
                RETURNING request.tenant_id, request.run_id, request.provider_code
            ),
            coverage_upsert AS (
                INSERT INTO crm_prospecting_provider_coverage
                    (tenant_id, campaign_id, provider_code, coverage_scope_hash,
                     last_successful_scan_at, last_run_id, last_result_count, last_response_hash, version)
                SELECT tenant_id, @campaignId, provider_code, @coverageScopeHash,
                       @completedAt, run_id, @resultCount, @responseHash, 1
                FROM finalized_request
                ON CONFLICT (tenant_id, campaign_id, provider_code, coverage_scope_hash)
                DO UPDATE SET
                    last_successful_scan_at = GREATEST(
                        crm_prospecting_provider_coverage.last_successful_scan_at,
                        EXCLUDED.last_successful_scan_at),
                    last_run_id = CASE
                        WHEN EXCLUDED.last_successful_scan_at >= crm_prospecting_provider_coverage.last_successful_scan_at
                        THEN EXCLUDED.last_run_id
                        ELSE crm_prospecting_provider_coverage.last_run_id
                    END,
                    last_result_count = CASE
                        WHEN EXCLUDED.last_successful_scan_at >= crm_prospecting_provider_coverage.last_successful_scan_at
                        THEN EXCLUDED.last_result_count
                        ELSE crm_prospecting_provider_coverage.last_result_count
                    END,
                    last_response_hash = CASE
                        WHEN EXCLUDED.last_successful_scan_at >= crm_prospecting_provider_coverage.last_successful_scan_at
                        THEN EXCLUDED.last_response_hash
                        ELSE crm_prospecting_provider_coverage.last_response_hash
                    END,
                    version = crm_prospecting_provider_coverage.version + 1
                RETURNING 1
            )
            SELECT COUNT(*)::integer FROM coverage_upsert;
            """;

        var rows = await client.Ado.SqlQueryAsync<int>(
            sql,
            new SugarParameter("@tenantId", currentTenant.TenantId),
            new SugarParameter("@requestId", requestId),
            new SugarParameter("@campaignId", campaignId),
            new SugarParameter("@coverageScopeHash", coverageScopeHash.ToUpperInvariant()),
            new SugarParameter("@completedAt", completedAt),
            new SugarParameter("@durationMs", durationMs),
            new SugarParameter("@resultCount", resultCount),
            new SugarParameter("@responseHash", responseHash.ToUpperInvariant()));
        cancellationToken.ThrowIfCancellationRequested();
        return rows.SingleOrDefault() == 1;
    }

    public async Task<DateTimeOffset?> GetActiveCooldownUntilAsync(
        string providerCode, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        providerCode = providerCode.Trim().ToLowerInvariant();
        cancellationToken.ThrowIfCancellationRequested();

        var rows = await CreateQueryable()
            .Where(row => row.ProviderCode == providerCode &&
                row.State == "RateLimited" &&
                row.RetryAfterAt != null &&
                row.RetryAfterAt > now)
            .OrderByDescending(row => row.RetryAfterAt)
            .Take(1)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault()?.RetryAfterAt;
    }

    public async Task EnsureSchemaConstraintsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DbClient.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("Provider request ledger constraints require PostgreSQL.");

        const string sql = """
            DO $$
            DECLARE
                table_oid regclass := to_regclass('crm_prospecting_provider_request');
                coverage_oid regclass := to_regclass('crm_prospecting_provider_coverage');
            BEGIN
                -- Serialize concurrent startup initializers across worker instances.
                PERFORM pg_advisory_xact_lock(hashtext('crm_prospecting_provider_request_schema'));
                IF table_oid IS NULL THEN
                    RETURN;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = table_oid AND conname = 'ck_crm_prospecting_provider_request_attempt') THEN
                    ALTER TABLE crm_prospecting_provider_request ADD CONSTRAINT ck_crm_prospecting_provider_request_attempt CHECK (attempt_no > 0) NOT VALID;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = table_oid AND conname = 'ck_crm_prospecting_provider_request_result_count') THEN
                    ALTER TABLE crm_prospecting_provider_request ADD CONSTRAINT ck_crm_prospecting_provider_request_result_count CHECK (result_count >= 0) NOT VALID;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = table_oid AND conname = 'ck_crm_prospecting_provider_request_duration') THEN
                    ALTER TABLE crm_prospecting_provider_request ADD CONSTRAINT ck_crm_prospecting_provider_request_duration CHECK (duration_ms IS NULL OR duration_ms >= 0) NOT VALID;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = table_oid AND conname = 'ck_crm_prospecting_provider_request_http_status') THEN
                    ALTER TABLE crm_prospecting_provider_request ADD CONSTRAINT ck_crm_prospecting_provider_request_http_status CHECK (http_status IS NULL OR http_status BETWEEN 100 AND 599) NOT VALID;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = table_oid AND conname = 'ck_crm_prospecting_provider_request_request_key') THEN
                    ALTER TABLE crm_prospecting_provider_request ADD CONSTRAINT ck_crm_prospecting_provider_request_request_key CHECK (request_key ~ '^[0-9A-Fa-f]{64}$') NOT VALID;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = table_oid AND conname = 'ck_crm_prospecting_provider_request_response_hash') THEN
                    ALTER TABLE crm_prospecting_provider_request ADD CONSTRAINT ck_crm_prospecting_provider_request_response_hash CHECK (response_hash IS NULL OR response_hash ~ '^[0-9A-Fa-f]{64}$') NOT VALID;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = table_oid AND conname = 'ck_crm_prospecting_provider_request_lifecycle') THEN
                    ALTER TABLE crm_prospecting_provider_request ADD CONSTRAINT ck_crm_prospecting_provider_request_lifecycle CHECK (
                        (state = 'Started' AND completed_at IS NULL)
                        OR (state <> 'Started' AND completed_at IS NOT NULL)
                    ) NOT VALID;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = table_oid AND conname = 'ck_crm_prospecting_provider_request_rate_limit') THEN
                    ALTER TABLE crm_prospecting_provider_request ADD CONSTRAINT ck_crm_prospecting_provider_request_rate_limit CHECK (
                        state <> 'RateLimited' OR retry_after_at IS NOT NULL
                    ) NOT VALID;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = table_oid AND conname = 'ck_crm_prospecting_provider_request_state') THEN
                    ALTER TABLE crm_prospecting_provider_request ADD CONSTRAINT ck_crm_prospecting_provider_request_state CHECK (
                        state IN ('Started','Succeeded','Failed','RateLimited','Cancelled')
                    ) NOT VALID;
                END IF;

                IF coverage_oid IS NOT NULL THEN
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = coverage_oid AND conname = 'ck_crm_prospecting_provider_coverage_result_count') THEN
                        ALTER TABLE crm_prospecting_provider_coverage ADD CONSTRAINT ck_crm_prospecting_provider_coverage_result_count CHECK (last_result_count >= 0) NOT VALID;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = coverage_oid AND conname = 'ck_crm_prospecting_provider_coverage_version') THEN
                        ALTER TABLE crm_prospecting_provider_coverage ADD CONSTRAINT ck_crm_prospecting_provider_coverage_version CHECK (version > 0) NOT VALID;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = coverage_oid AND conname = 'ck_crm_prospecting_provider_coverage_scope_hash') THEN
                        ALTER TABLE crm_prospecting_provider_coverage ADD CONSTRAINT ck_crm_prospecting_provider_coverage_scope_hash CHECK (coverage_scope_hash ~ '^[0-9A-Fa-f]{64}$') NOT VALID;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = coverage_oid AND conname = 'ck_crm_prospecting_provider_coverage_response_hash') THEN
                        ALTER TABLE crm_prospecting_provider_coverage ADD CONSTRAINT ck_crm_prospecting_provider_coverage_response_hash CHECK (last_response_hash IS NULL OR last_response_hash ~ '^[0-9A-Fa-f]{64}$') NOT VALID;
                    END IF;
                END IF;
            END
            $$;
            """;
        await DbClient.Ado.ExecuteCommandAsync(sql);
    }

    public async Task<int> RecoverStaleStartedAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default)
    {
        if (minimumAge <= TimeSpan.Zero || minimumAge > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(minimumAge), "Stale request age must be positive and at most seven days.");
        cancellationToken.ThrowIfCancellationRequested();

        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("Stale provider request recovery requires PostgreSQL.");

        var currentTenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for provider request recovery.");
        var ageMilliseconds = checked((long)Math.Ceiling(minimumAge.TotalMilliseconds));
        const string sql = """
            WITH recovery_clock AS MATERIALIZED (
                SELECT clock_timestamp() AS now
            )
            UPDATE crm_prospecting_provider_request AS request
            SET state = 'Failed',
                completed_at = recovery_clock.now,
                duration_ms = GREATEST(
                    0,
                    FLOOR(EXTRACT(EPOCH FROM (recovery_clock.now - request.started_at)) * 1000)::bigint
                ),
                failure_code = 'worker_interrupted',
                failure_summary = 'Provider request was interrupted before a terminal outcome.'
            FROM recovery_clock
            WHERE request.tenant_id = @tenantId
              AND request.state = 'Started'
              AND request.started_at <= recovery_clock.now - (@ageMilliseconds * INTERVAL '1 millisecond')
              AND (
                    NOT EXISTS (
                        SELECT 1
                        FROM crm_prospecting_run AS run
                        WHERE run.tenant_id = request.tenant_id
                          AND run.basic_id = request.run_id
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM crm_prospecting_run AS run
                        WHERE run.tenant_id = request.tenant_id
                          AND run.basic_id = request.run_id
                          AND (
                                run.status NOT IN (@runningStatus, @cancelRequestedStatus)
                                OR run.lease_expires_at IS NOT NULL
                                   AND run.lease_expires_at <= recovery_clock.now
                          )
                    )
              )
            RETURNING request.basic_id AS "BasicId";
            """;

        var recovered = await client.Ado.SqlQueryAsync<long>(
            sql,
            new SugarParameter("@tenantId", currentTenant.TenantId),
            new SugarParameter("@ageMilliseconds", ageMilliseconds),
            new SugarParameter("@runningStatus", (int)CrmProspectingRunStatus.Running),
            new SugarParameter("@cancelRequestedStatus", (int)CrmProspectingRunStatus.CancelRequested));
        return recovered.Count;
    }

    public async Task<SysCrmProspectingProviderRequest?> GetAttemptAsync(
        long runId, string providerCode, int attemptNo,
        CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        providerCode = providerCode.Trim().ToLowerInvariant();
        if (attemptNo <= 0) throw new ArgumentOutOfRangeException(nameof(attemptNo));
        cancellationToken.ThrowIfCancellationRequested();

        var rows = await CreateQueryable()
            .Where(row => row.RunId == runId && row.ProviderCode == providerCode && row.AttemptNo == attemptNo)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (rows.Count > 1)
            throw new InvalidOperationException("The tenant contains duplicate provider request attempt identities.");
        return rows.FirstOrDefault();
    }

    private static string? Bound(string? value, int maxLength)
    {
        var sanitized = SanitizeSummary(value);
        return sanitized is { Length: > 0 } ? sanitized[..Math.Min(sanitized.Length, maxLength)] : null;
    }

    private static string? SanitizeSummary(string? value)
    {
        var sanitized = CrmProspectingDiagnosticSanitizer.Sanitize(value, 1000);
        return sanitized.Length == 0 ? null : sanitized;
    }
}
