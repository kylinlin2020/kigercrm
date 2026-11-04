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
        if (string.IsNullOrWhiteSpace(value)) return null;
        var builder = new StringBuilder(Math.Min(value.Length, 1000));
        foreach (var character in value)
        {
            if (!char.IsControl(character) || character is '\r' or '\n' or '\t')
                builder.Append(char.IsControl(character) ? ' ' : character);
            if (builder.Length >= 1000) break;
        }

        // Never retain URL query/fragment values in operational diagnostics.
        var sanitized = builder.ToString();
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(?i)\b(authorization\s*[:=]\s*(?:bearer|basic)\s+)[^\s,;]+",
            "$1[REDACTED]");
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(?i)\b(api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|password|secret)\s*[:=]\s*[^\s&;,]+",
            "$1=[REDACTED]");
        // Never retain URL query/fragment values in operational diagnostics.
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(?i)(https?://[^\s?#]+)[^\s]*",
            "$1");
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(?i)\b(Bearer|Basic)\s+[A-Za-z0-9._~+/=-]+",
            "$1 [REDACTED]");
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(?i)\b(api[_-]?key|access[_-]?token|refresh[_-]?token|token|secret|password|client_secret)\s*[:=]\s*[""']?[^\s&,""']+",
            "$1=[REDACTED]");
        return sanitized;
    }
}
