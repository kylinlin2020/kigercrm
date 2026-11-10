using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

/// <summary>
/// PostgreSQL provider-step checkpoint repository. Every mutation is scoped to the current
/// tenant and fenced by the live parent Run lease. Checkpoint writes require the caller's transaction.
/// </summary>
public sealed class CrmProspectingRunProviderExecutionRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingRunProviderExecution>(clientResolver), ICrmProspectingRunProviderExecutionRepository
{
    public const int MaxCheckpointBytes = 1_048_576;
    private const int MaxFailureSummaryLength = 1000;

    public async Task<IReadOnlyList<SysCrmProspectingRunProviderExecution>> GetByRunAsync(
        long runId, CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for provider execution reads.");
        return await CreateQueryable()
            .Where(row => row.TenantId == tenant.TenantId && row.RunId == runId)
            .OrderBy(row => row.PlanOrdinal)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SysCrmProspectingRunProviderExecution>> EnsurePlanMaterializedAsync(
        long runId, Guid leaseToken, long fencingVersion, CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Run lease token is required.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
        cancellationToken.ThrowIfCancellationRequested();
        var (client, tenantId) = GetPostgresTenant();
        if (client.Ado.Transaction is not null)
            throw new InvalidOperationException("Plan materialization owns its transaction and cannot run inside another transaction.");

        const string lockRunSql = """
            SELECT run.*
            FROM crm_prospecting_run AS run
            WHERE run.tenant_id = @tenantId
              AND run.basic_id = @runId
              AND run.status = @runningStatus
              AND run.lease_token = @leaseToken
              AND run.fencing_version = @fencingVersion
              AND run.lease_expires_at > clock_timestamp()
            FOR UPDATE;
            """;

        client.Ado.BeginTran();
        try
        {
            var runs = await client.Ado.SqlQueryAsync<SysCrmProspectingRun>(lockRunSql,
                new SugarParameter("@tenantId", tenantId),
                new SugarParameter("@runId", runId),
                new SugarParameter("@runningStatus", (int)XiHan.BasicApp.Crm.Domain.Entities.Enums.CrmProspectingRunStatus.Running),
                new SugarParameter("@leaseToken", leaseToken),
                new SugarParameter("@fencingVersion", fencingVersion));
            var run = runs.SingleOrDefault()
                ?? throw new InvalidOperationException("The exact live Run lease is required to materialize its provider plan.");
            if (run.StrategyVersion <= 0 || string.IsNullOrWhiteSpace(run.PayloadHash)
                || run.PayloadHash.Length != 64 || !run.PayloadHash.All(Uri.IsHexDigit))
                throw new InvalidOperationException("The Run snapshot does not contain a valid immutable strategy version and payload hash.");

            using var document = JsonDocument.Parse(run.SnapshotProviderPlanJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("The immutable provider plan snapshot must be a JSON array.");
            var providers = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException("Every immutable provider plan entry must be a string.");
                var code = entry.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(code) || code.Length > 80 || code.Any(char.IsControl))
                    throw new InvalidOperationException("The immutable provider plan contains an invalid provider code.");
                if (!seen.Add(code))
                    throw new InvalidOperationException("The immutable provider plan contains duplicate provider codes.");
                providers.Add(code.ToLowerInvariant());
                if (providers.Count > 32)
                    throw new InvalidOperationException("The immutable provider plan exceeds the 32-provider execution limit.");
            }
            if (providers.Count == 0)
                throw new InvalidOperationException("The immutable provider plan cannot be empty.");

            var existing = await CreateQueryable()
                .Where(row => row.TenantId == tenantId && row.RunId == runId && row.PlanVersion == run.StrategyVersion)
                .OrderBy(row => row.PlanOrdinal)
                .ToListAsync(cancellationToken);
            if (existing.Count > 0)
            {
                if (existing.Count != providers.Count)
                    throw new InvalidOperationException("A partially materialized provider plan was found; refusing to mutate an immutable plan.");
                for (var ordinal = 0; ordinal < providers.Count; ordinal++)
                {
                    var expectedFingerprint = CreatePlanFingerprint(run.PayloadHash, run.StrategyVersion, providers[ordinal]);
                    var row = existing[ordinal];
                    if (row.PlanOrdinal != ordinal || !string.Equals(row.ProviderCode, providers[ordinal], StringComparison.Ordinal)
                        || !string.Equals(row.RequestFingerprint, expectedFingerprint, StringComparison.Ordinal))
                        throw new InvalidOperationException("Persisted provider execution rows do not match the immutable Run snapshot.");
                }
                client.Ado.CommitTran();
                return existing;
            }

            var materialized = new List<SysCrmProspectingRunProviderExecution>(providers.Count);
            for (var ordinal = 0; ordinal < providers.Count; ordinal++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var code = providers[ordinal];
                var row = new SysCrmProspectingRunProviderExecution
                {
                    TenantId = tenantId,
                    RunId = runId,
                    PlanVersion = run.StrategyVersion,
                    PlanOrdinal = ordinal,
                    ProviderCode = code,
                    RequestFingerprint = CreatePlanFingerprint(run.PayloadHash, run.StrategyVersion, code),
                    State = "Queued",
                    AttemptCount = 0,
                    CheckpointBytes = 0,
                    RawRecordCount = 0,
                    ProjectedRecordCount = 0,
                    FailureCount = 0
                };
                await AddAsync(row, cancellationToken);
                materialized.Add(row);
            }
            client.Ado.CommitTran();
            return materialized;
        }
        catch
        {
            client.Ado.RollbackTran();
            throw;
        }
    }

    private static string CreatePlanFingerprint(string payloadHash, int planVersion, string providerCode)
    {
        var canonical = $"kiger-provider-plan-v1\n{payloadHash.ToUpperInvariant()}\n{planVersion}\n{providerCode}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public async Task<bool> TryStartAsync(
        long executionId, long runId, Guid leaseToken, long fencingVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(executionId, runId, leaseToken, fencingVersion);
        cancellationToken.ThrowIfCancellationRequested();
        var (client, tenantId) = GetPostgresTenant();
        const string sql = """
            WITH locked_run AS MATERIALIZED (
                SELECT run.basic_id
                FROM crm_prospecting_run AS run
                WHERE run.tenant_id = @tenantId
                  AND run.basic_id = @runId
                  AND run.status = @runningStatus
                  AND run.lease_token = @leaseToken
                  AND run.fencing_version = @fencingVersion
                  AND run.lease_expires_at > clock_timestamp()
                FOR UPDATE
            )
            UPDATE crm_prospecting_run_provider_execution AS step
            SET state = 'Started',
                attempt_count = step.attempt_count + 1,
                started_at = clock_timestamp(),
                completed_at = NULL,
                failure_code = NULL,
                failure_summary = NULL
            WHERE step.tenant_id = @tenantId
              AND step.basic_id = @executionId
              AND step.run_id = @runId
              AND step.state = 'Queued'
              AND EXISTS (SELECT 1 FROM locked_run);
            """;
        return await client.Ado.ExecuteCommandAsync(sql,
            new SugarParameter("@tenantId", tenantId),
            new SugarParameter("@executionId", executionId),
            new SugarParameter("@runId", runId),
            new SugarParameter("@runningStatus", (int)XiHan.BasicApp.Crm.Domain.Entities.Enums.CrmProspectingRunStatus.Running),
            new SugarParameter("@leaseToken", leaseToken),
            new SugarParameter("@fencingVersion", fencingVersion)) == 1;
    }

    public async Task<bool> TrySaveCheckpointAsync(
        long executionId, long runId, Guid leaseToken, long fencingVersion,
        string checkpointJson, int rawRecordCount, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(executionId, runId, leaseToken, fencingVersion);
        ArgumentNullException.ThrowIfNull(checkpointJson);
        try { using var document = JsonDocument.Parse(checkpointJson); }
        catch (JsonException ex) { throw new ArgumentException("Provider checkpoint must be valid JSON.", nameof(checkpointJson), ex); }
        if (rawRecordCount < 0) throw new ArgumentOutOfRangeException(nameof(rawRecordCount));
        var bytes = Encoding.UTF8.GetBytes(checkpointJson);
        if (bytes.Length is 0 or > MaxCheckpointBytes)
            throw new ArgumentOutOfRangeException(nameof(checkpointJson), $"Checkpoint must be between 1 and {MaxCheckpointBytes} UTF-8 bytes.");
        cancellationToken.ThrowIfCancellationRequested();
        var (client, tenantId) = GetPostgresTenant();
        if (client.Ado.Transaction is not null)
            throw new InvalidOperationException("Provider checkpoint persistence owns its transaction and cannot run inside another transaction.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        const string sql = """
            WITH locked_run AS MATERIALIZED (
                SELECT run.basic_id
                FROM crm_prospecting_run AS run
                WHERE run.tenant_id = @tenantId
                  AND run.basic_id = @runId
                  AND run.status = @runningStatus
                  AND run.lease_token = @leaseToken
                  AND run.fencing_version = @fencingVersion
                  AND run.lease_expires_at > clock_timestamp()
                FOR UPDATE
            )
            UPDATE crm_prospecting_run_provider_execution AS step
            SET state = 'Checkpointed',
                checkpoint_json = @checkpointJson,
                checkpoint_hash = @checkpointHash,
                checkpoint_bytes = @checkpointBytes,
                raw_record_count = @rawRecordCount
            WHERE step.tenant_id = @tenantId
              AND step.basic_id = @executionId
              AND step.run_id = @runId
              AND step.state = 'Started'
              AND step.checkpoint_json IS NULL
              AND EXISTS (SELECT 1 FROM locked_run);
            """;
        client.Ado.BeginTran();
        try
        {
            var changed = await client.Ado.ExecuteCommandAsync(sql,
                new SugarParameter("@checkpointJson", checkpointJson),
                new SugarParameter("@checkpointHash", hash),
                new SugarParameter("@checkpointBytes", bytes.Length),
                new SugarParameter("@rawRecordCount", rawRecordCount),
                new SugarParameter("@tenantId", tenantId),
                new SugarParameter("@executionId", executionId),
                new SugarParameter("@runId", runId),
                new SugarParameter("@runningStatus", (int)XiHan.BasicApp.Crm.Domain.Entities.Enums.CrmProspectingRunStatus.Running),
                new SugarParameter("@leaseToken", leaseToken),
                new SugarParameter("@fencingVersion", fencingVersion));
            client.Ado.CommitTran();
            return changed == 1;
        }
        catch
        {
            client.Ado.RollbackTran();
            throw;
        }
    }


    public async Task<bool> TryMarkInterruptedNeedsReviewAsync(
        long executionId, long runId, Guid leaseToken, long fencingVersion,
        string expectedState, string failureCode, string safeFailureSummary,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(executionId, runId, leaseToken, fencingVersion);
        if (expectedState is not ("Started" or "Checkpointed"))
            throw new ArgumentException("Only interrupted Started or Checkpointed steps can be reconciled automatically.", nameof(expectedState));
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeFailureSummary);
        cancellationToken.ThrowIfCancellationRequested();
        var (client, tenantId) = GetPostgresTenant();
        if (client.Ado.Transaction is not null)
            throw new InvalidOperationException("Interrupted-step reconciliation owns its transaction and cannot run inside another transaction.");

        const string sql = """
            WITH locked_run AS MATERIALIZED (
                SELECT run.basic_id
                FROM crm_prospecting_run AS run
                WHERE run.tenant_id = @tenantId
                  AND run.basic_id = @runId
                  AND run.status = @runningStatus
                  AND run.lease_token = @leaseToken
                  AND run.fencing_version = @fencingVersion
                  AND run.lease_expires_at > clock_timestamp()
                FOR UPDATE
            )
            UPDATE crm_prospecting_run_provider_execution AS step
            SET state = 'NeedsReview',
                failure_code = @failureCode,
                failure_summary = @failureSummary,
                completed_at = clock_timestamp()
            WHERE step.tenant_id = @tenantId
              AND step.basic_id = @executionId
              AND step.run_id = @runId
              AND step.state = @expectedState
              AND EXISTS (SELECT 1 FROM locked_run);
            """;
        client.Ado.BeginTran();
        try
        {
            var changed = await client.Ado.ExecuteCommandAsync(sql,
                new SugarParameter("@tenantId", tenantId),
                new SugarParameter("@executionId", executionId),
                new SugarParameter("@runId", runId),
                new SugarParameter("@expectedState", expectedState),
                new SugarParameter("@failureCode", Bound(failureCode, 80)),
                new SugarParameter("@failureSummary", Bound(safeFailureSummary, MaxFailureSummaryLength)),
                new SugarParameter("@runningStatus", (int)XiHan.BasicApp.Crm.Domain.Entities.Enums.CrmProspectingRunStatus.Running),
                new SugarParameter("@leaseToken", leaseToken),
                new SugarParameter("@fencingVersion", fencingVersion));
            client.Ado.CommitTran();
            return changed == 1;
        }
        catch
        {
            client.Ado.RollbackTran();
            throw;
        }
    }

    public async Task<bool> TryTransitionAsync(
        long executionId, long runId, Guid leaseToken, long fencingVersion,
        string expectedState, string nextState, int? projectedRecordCount = null, int? failureCount = null,
        string? failureCode = null, string? safeFailureSummary = null,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(executionId, runId, leaseToken, fencingVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedState);
        ArgumentException.ThrowIfNullOrWhiteSpace(nextState);
        if (expectedState.Length > 24 || nextState.Length > 24)
            throw new ArgumentOutOfRangeException(nameof(nextState));
        if (projectedRecordCount is < 0 || failureCount is < 0)
            throw new ArgumentOutOfRangeException(nameof(projectedRecordCount));
        var allowed = (expectedState, nextState) switch
        {
            ("Checkpointed", "Projecting") => true,
            ("Projecting", "Succeeded") => true,
            ("Projecting", "Failed") => true,
            ("Started", "Failed") => true,
            ("Started", "NeedsReview") => true,
            ("Checkpointed", "NeedsReview") => true,
            ("Projecting", "NeedsReview") => true,
            ("Queued", "Skipped") => true,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException("The requested provider execution transition is not allowed.");
        if (nextState == "Succeeded" && (projectedRecordCount is null || failureCount.GetValueOrDefault() != 0))
            throw new ArgumentException("Succeeded requires a projected record count and zero failures.");
        cancellationToken.ThrowIfCancellationRequested();
        var (client, tenantId) = GetPostgresTenant();
        if (nextState is "Succeeded" or "Failed" or "NeedsReview" or "Skipped")
        {
            if (client.Ado.Transaction is null)
                throw new InvalidOperationException("Terminal provider execution transitions require the caller's active transaction.");
        }
        const string sql = """
            WITH locked_run AS MATERIALIZED (
                SELECT run.basic_id
                FROM crm_prospecting_run AS run
                WHERE run.tenant_id = @tenantId
                  AND run.basic_id = @runId
                  AND run.status = @runningStatus
                  AND run.lease_token = @leaseToken
                  AND run.fencing_version = @fencingVersion
                  AND run.lease_expires_at > clock_timestamp()
                FOR UPDATE
            )
            UPDATE crm_prospecting_run_provider_execution AS step
            SET state = @nextState,
                projected_record_count = COALESCE(@projectedRecordCount, step.projected_record_count),
                failure_count = COALESCE(@failureCount, step.failure_count),
                failure_code = @failureCode,
                failure_summary = @failureSummary,
                completed_at = CASE WHEN @isTerminal THEN clock_timestamp() ELSE NULL END
            WHERE step.tenant_id = @tenantId
              AND step.basic_id = @executionId
              AND step.run_id = @runId
              AND step.state = @expectedState
              AND EXISTS (SELECT 1 FROM locked_run);
            """;
        var terminal = nextState is "Succeeded" or "Failed" or "NeedsReview" or "Skipped";
        return await client.Ado.ExecuteCommandAsync(sql,
            new SugarParameter("@nextState", nextState),
            new SugarParameter("@projectedRecordCount", projectedRecordCount),
            new SugarParameter("@failureCount", failureCount),
            new SugarParameter("@failureCode", Bound(failureCode, 80)),
            new SugarParameter("@failureSummary", Bound(safeFailureSummary, MaxFailureSummaryLength)),
            new SugarParameter("@isTerminal", terminal),
            new SugarParameter("@tenantId", tenantId),
            new SugarParameter("@executionId", executionId),
            new SugarParameter("@runId", runId),
            new SugarParameter("@expectedState", expectedState),
            new SugarParameter("@runningStatus", (int)XiHan.BasicApp.Crm.Domain.Entities.Enums.CrmProspectingRunStatus.Running),
            new SugarParameter("@leaseToken", leaseToken),
            new SugarParameter("@fencingVersion", fencingVersion)) == 1;
    }

    private (ISqlSugarClient Client, long TenantId) GetPostgresTenant()
    {
        var client = DbClient;
        if (client.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            throw new NotSupportedException("Provider execution checkpoints require PostgreSQL.");
        var tenant = AsyncLocalCurrentTenantAccessor.Instance.Current
            ?? throw new InvalidOperationException("A current tenant context is required for provider execution writes.");
        return (client, tenant.TenantId ?? 0);
    }

    private static void ValidateIdentity(long executionId, long runId, Guid leaseToken, long fencingVersion)
    {
        if (executionId <= 0) throw new ArgumentOutOfRangeException(nameof(executionId));
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Run lease token is required.", nameof(leaseToken));
        if (fencingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fencingVersion));
    }

    private static string? Bound(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sanitized = new string(value.Where(ch => !char.IsControl(ch) || ch is '\t').ToArray()).Trim();
        return sanitized.Length > max ? sanitized[..max] : sanitized;
    }
}
