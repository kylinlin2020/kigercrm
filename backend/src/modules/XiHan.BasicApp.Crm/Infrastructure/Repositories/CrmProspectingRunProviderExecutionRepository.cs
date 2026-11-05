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
        if (client.Ado.Transaction is null)
            throw new InvalidOperationException("Provider checkpoint persistence requires the caller's active transaction.");
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
        return await client.Ado.ExecuteCommandAsync(sql,
            new SugarParameter("@checkpointJson", checkpointJson),
            new SugarParameter("@checkpointHash", hash),
            new SugarParameter("@checkpointBytes", bytes.Length),
            new SugarParameter("@rawRecordCount", rawRecordCount),
            new SugarParameter("@tenantId", tenantId),
            new SugarParameter("@executionId", executionId),
            new SugarParameter("@runId", runId),
            new SugarParameter("@runningStatus", (int)XiHan.BasicApp.Crm.Domain.Entities.Enums.CrmProspectingRunStatus.Running),
            new SugarParameter("@leaseToken", leaseToken),
            new SugarParameter("@fencingVersion", fencingVersion)) == 1;
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
        return (client, tenant.TenantId);
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
