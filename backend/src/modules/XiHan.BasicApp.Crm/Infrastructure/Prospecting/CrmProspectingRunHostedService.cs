using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Domain.Services;
using XiHan.BasicApp.Saas.Domain.DomainServices;
using XiHan.Framework.Caching.Distributed.Abstracts;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Tasks.BackgroundServices;
using XiHan.Framework.Uow;
using XiHan.Framework.Uow.Options;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>Redis queue envelope for a tenant-scoped prospecting Run.</summary>
public sealed class CrmProspectingRunMessage : IBackgroundTaskItem
{
    public long RunId { get; init; }
    public long TenantId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    [JsonIgnore]
    public string TaskId => RunId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [JsonIgnore]
    public object? Data => null;
}

/// <summary>
/// Executes approved prospecting provider plans with tenant-scoped leases and fenced child writes.
/// Keep one active worker until multi-instance PostgreSQL acceptance is complete.
/// </summary>
public sealed class CrmProspectingRunHostedService : XiHanBackgroundServiceBase<CrmProspectingRunHostedService>
{
    private static readonly TimeSpan RunLeaseDuration = TimeSpan.FromMinutes(10);
    private static readonly string LeaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRedisDelayQueue<CrmProspectingRunMessage> _queue;

    public CrmProspectingRunHostedService(
        IServiceScopeFactory scopeFactory,
        IRedisDelayQueue<CrmProspectingRunMessage> queue,
        IOptions<XiHanBackgroundServiceOptions> options,
        ILogger<CrmProspectingRunHostedService> logger)
        : base(logger, options, BuildConfig(options))
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverPendingAsync(stoppingToken);
        await base.ExecuteAsync(stoppingToken);
    }

    protected override async Task<IEnumerable<IBackgroundTaskItem>> FetchWorkItemsAsync(int maxCount, CancellationToken cancellationToken)
        => await _queue.DequeueDueAsync(maxCount, cancellationToken);

    protected override async Task ProcessItemAsync(IBackgroundTaskItem item, CancellationToken cancellationToken)
    {
        var message = (CrmProspectingRunMessage)item;
        if (message.RunId <= 0 || message.TenantId <= 0)
        {
            Logger.LogError(
                "Discarding malformed prospecting queue message with RunId {RunId} and TenantId {TenantId}",
                message.RunId,
                message.TenantId);
            return;
        }

        Guid? activeLeaseToken = null;
        long activeFencingVersion = 0;
        CancellationTokenSource? leaseHeartbeatCts = null;
        Task? leaseHeartbeatTask = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(message.TenantId);

            var requestLedger = scope.ServiceProvider.GetRequiredService<ICrmProspectingProviderRequestRepository>();
            await requestLedger.EnsureSchemaConstraintsAsync(cancellationToken);
            var runs = scope.ServiceProvider.GetRequiredService<ICrmProspectingRunRepository>();
            var run = await runs.TryClaimLeaseAsync(message.RunId, LeaseOwner, RunLeaseDuration, cancellationToken);
            if (run is null) return;
            if (run.LeaseToken is not Guid leaseToken || leaseToken == Guid.Empty || run.FencingVersion <= 0)
                throw new InvalidOperationException("The claimed Run did not return a valid lease token and fencing version.");
            activeLeaseToken = leaseToken;
            activeFencingVersion = run.FencingVersion;

            // Keep a lease alive independently of provider/record processing boundaries.
            // The heartbeat has its own tenant scope and repository instance; if ownership
            // is lost it cancels this Run so no further non-fenced work is started.
            leaseHeartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationToken = leaseHeartbeatCts.Token;
            leaseHeartbeatTask = RunLeaseHeartbeatAsync(
                message.TenantId, run.BasicId, leaseToken, activeFencingVersion, leaseHeartbeatCts);

            // Provider codes are global identities across capability contracts. Never silently
            // choose the first registration: a sync/async collision could route a Run to the wrong adapter.
            var providerRegistry = CrmProspectingProviderRegistry.Create(
                scope.ServiceProvider.GetServices<ICrmProspectingProvider>(),
                scope.ServiceProvider.GetServices<ICrmProspectingAsyncExtractionProvider>());
            var providers = providerRegistry.Synchronous;
            // Materialize once from the immutable Run snapshot. On subsequent delivery/recovery,
            // the persisted execution rows are authoritative and are verified against that snapshot;
            // mutable Campaign/Strategy configuration is never consulted here.
            var providerExecutions = scope.ServiceProvider.GetRequiredService<ICrmProspectingRunProviderExecutionRepository>();
            var persistedPlan = await providerExecutions.EnsurePlanMaterializedAsync(
                run.BasicId, leaseToken, activeFencingVersion, cancellationToken);
            var plan = persistedPlan.OrderBy(step => step.PlanOrdinal)
                .Select(step => step.ProviderCode)
                .ToList();
            if (plan.Count == 0) throw new InvalidOperationException("The persisted provider execution plan contains no usable provider codes.");

            // Preflight the complete immutable provider plan before any adapter can perform
            // network I/O or create request-ledger attempts. Async-only adapters are deliberately
            // unsupported by this Run worker until durable Run continuation and an extraction
            // consumer are wired; detecting them inside the execution loop could leave earlier
            // synchronous providers externally called while their in-memory results are discarded.
            var unsupportedAsyncProviders = plan
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(providerCode =>
                    !providers.ContainsKey(providerCode) &&
                    providerRegistry.Asynchronous.ContainsKey(providerCode))
                .ToArray();
            if (unsupportedAsyncProviders.Length > 0)
                throw new InvalidOperationException(
                    $"The Run plan contains asynchronous providers that are not yet supported by durable Run orchestration: {string.Join(", ", unsupportedAsyncProviders)}.");

            var excludedDomains = ParseStringArray(run.SnapshotExcludedDomainsJson, "excluded domains", allowNull: true);
            var raw = new List<CrmProspectingRawEnvelope>();
            var checkpointedSteps = new List<(long ExecutionId, string State, int FailureCount, bool IsComplete, int RecordCount)>();
            // Do not advance incremental coverage while provider records are only in memory.
            // Candidate/evidence persistence must finish first so a worker crash cannot skip data.
            var pendingCoverage = new List<(long AttemptId, string RequestKey, int ResultCount, string ResponseHash, long DurationMs)>();
            long estimatedRawJsonBytes = 2; // JSON array delimiters
            const int maxRawRecordsPerRun = 2000;
            const long maxRawJsonBytes = 5_000_000;
            const long maxRawRecordJsonBytes = 64_000;
            var failures = 0;
            var warnings = new List<string>();

            foreach (var providerCode in plan.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = await runs.GetByIdAsync(run.BasicId, cancellationToken);
                if (current?.Status == CrmProspectingRunStatus.CancelRequested)
                    break;
                await RenewLeaseOrThrowAsync(runs, run.BasicId, leaseToken, activeFencingVersion, cancellationToken);

                var executionStep = persistedPlan.Single(step =>
                    string.Equals(step.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase));
                if (executionStep.State == "Started")
                {
                    // The external outcome is ambiguous: no response checkpoint proves what was returned.
                    if (!await providerExecutions.TryMarkInterruptedNeedsReviewAsync(
                        executionStep.BasicId, run.BasicId, leaseToken, activeFencingVersion,
                        executionStep.State, "provider_execution_interrupted",
                        "Worker restarted while provider outcome was ambiguous; automatic re-invocation was blocked.",
                        cancellationToken))
                        throw new InvalidOperationException($"Interrupted provider execution step '{providerCode}' could not be fenced into NeedsReview.");
                    failures++;
                    AddBoundedWarning(warnings, $"{providerCode}: interrupted provider execution requires review; no duplicate external request was issued.");
                    continue;
                }
                else if (executionStep.State is "Checkpointed" or "Projecting")
                {
                    var checkpointJson = executionStep.CheckpointJson
                        ?? throw new InvalidOperationException($"Provider checkpoint is missing for '{providerCode}'.");
                    var checkpointBytes = System.Text.Encoding.UTF8.GetByteCount(checkpointJson);
                    var checkpointHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(checkpointJson)));
                    if (checkpointBytes != executionStep.CheckpointBytes ||
                        !string.Equals(checkpointHash, executionStep.CheckpointHash, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Provider checkpoint integrity validation failed for '{providerCode}'.");
                    var checkpoint = JsonSerializer.Deserialize<CrmProspectingProviderResultCheckpoint>(checkpointJson)
                        ?? throw new InvalidOperationException($"Provider checkpoint payload is invalid for '{providerCode}'.");
                    if (checkpoint.Records is null || checkpoint.Records.Count != executionStep.RawRecordCount ||
                        checkpoint.Records.Any(record => record is null) || checkpoint.FailureCount < 0)
                        throw new InvalidOperationException($"Provider checkpoint record count or shape is invalid for '{providerCode}'.");
                    var replayFailures = checkpoint.FailureCount;
                    var replayedRecordCount = 0;
                    foreach (var checkpointRecord in checkpoint.Records)
                    {
                        if (raw.Count >= maxRawRecordsPerRun ||
                            !IsWithinRawRecordLimits(checkpointRecord, out var replayRecordBytes) ||
                            replayRecordBytes > maxRawRecordJsonBytes ||
                            estimatedRawJsonBytes + replayRecordBytes + 1 > maxRawJsonBytes)
                        {
                            replayFailures++;
                            continue;
                        }
                        raw.Add(new CrmProspectingRawEnvelope(providerCode, checkpointRecord));
                        estimatedRawJsonBytes += replayRecordBytes + 1;
                        replayedRecordCount++;
                    }
                    checkpointedSteps.Add((executionStep.BasicId, executionStep.State,
                        replayFailures, checkpoint.IsComplete, replayedRecordCount));
                    failures += replayFailures;
                    if (replayFailures > 0)
                        AddBoundedWarning(warnings, $"{providerCode}: replayed checkpoint contains incomplete or rejected records.");
                    continue;
                }
                else if (executionStep.State == "Succeeded")
                {
                    continue;
                }
                else if (executionStep.State != "Queued")
                {
                    failures++;
                    AddBoundedWarning(warnings, $"{providerCode}: provider execution is in terminal state '{executionStep.State}' and was not re-invoked.");
                    continue;
                }

                if (!providers.TryGetValue(providerCode, out var provider))
                {
                    // Async adapters are registered as a separate capability, but this Run worker
                    // currently executes only synchronous providers. Fail closed instead of counting
                    // an unexecuted async provider as an ordinary provider outage and completing a
                    // misleading partial Run. The durable extraction worker must own this path first.
                    if (providerRegistry.Asynchronous.ContainsKey(providerCode))
                        throw new InvalidOperationException(
                            $"Provider '{providerCode}' requires the asynchronous extraction worker, which is not yet wired to Run orchestration.");

                    failures++;
                    AddBoundedWarning(warnings, $"Provider is not registered: {providerCode}");
                    continue;
                }

                var cooldownUntil = await requestLedger.GetActiveCooldownUntilAsync(
                    providerCode, DateTimeOffset.UtcNow, cancellationToken);
                if (cooldownUntil is DateTimeOffset activeCooldown)
                {
                    failures++;
                    AddBoundedWarning(warnings,
                        $"{providerCode}: provider cooldown is active until {activeCooldown:O}; request was skipped.");
                    continue;
                }

                var request = new CrmProspectingProviderRequest(
                    run.BasicId,
                    run.SnapshotSearchObjective,
                    run.SnapshotProductScope ?? string.Empty,
                    run.SnapshotTargetMarkets ?? string.Empty,
                    run.SnapshotIdealCustomerProfile ?? string.Empty,
                    "{}",
                    excludedDomains);
                var requestKey = CrmProspectingRequestFingerprint.Create(providerCode, request);
                var coverageCheckpoint = await requestLedger.GetCoverageCheckpointAsync(
                    run.CampaignId, providerCode, requestKey, cancellationToken);
                if (coverageCheckpoint is not null)
                {
                    // Keep a 48-hour overlap to tolerate publication/update delays and clock skew.
                    // Providers that support CoverageSinceUtc use this lower bound; others ignore it.
                    request = request with
                    {
                        CoverageSinceUtc = coverageCheckpoint.LastSuccessfulScanAt.AddHours(-48)
                    };
                }

                // The Started row is durable before the provider is allowed to perform network I/O.
                // If ledger persistence fails, fail the Run rather than making an untracked request.
                if (executionStep.State != "Queued" ||
                    !await providerExecutions.TryStartAsync(
                        executionStep.BasicId, run.BasicId, leaseToken, activeFencingVersion, cancellationToken))
                    throw new InvalidOperationException($"Provider execution step '{providerCode}' could not be started under the live Run lease.");

                var attempt = await requestLedger.CreateStartedAsync(
                    run.BasicId, providerCode, requestKey, DateTimeOffset.UtcNow, cancellationToken);
                var providerTimer = System.Diagnostics.Stopwatch.StartNew();
                CrmProspectingProviderResult result;
                try
                {
                    result = await provider.SearchAsync(request, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    providerTimer.Stop();
                    if (!await requestLedger.TryFinalizeAsync(
                        attempt.BasicId, "Cancelled", DateTimeOffset.UtcNow, providerTimer.ElapsedMilliseconds,
                        failureCode: "operation_cancelled", failureSummary: "Provider request was cancelled.",
                        cancellationToken: CancellationToken.None))
                        throw new InvalidOperationException("The cancelled provider request ledger attempt could not be finalized.");
                    await MarkProviderStepFailedAsync(scope.ServiceProvider, providerExecutions,
                        executionStep.BasicId, run.BasicId, leaseToken, activeFencingVersion,
                        "operation_cancelled", "Provider request was cancelled.", CancellationToken.None);
                    throw;
                }
                catch (CrmProspectingProviderRateLimitException ex)
                {
                    providerTimer.Stop();
                    if (!await requestLedger.TryFinalizeAsync(
                        attempt.BasicId, "RateLimited", DateTimeOffset.UtcNow, providerTimer.ElapsedMilliseconds,
                        httpStatus: ex.HttpStatus, failureCode: "provider_rate_limited",
                        failureSummary: CrmProspectingDiagnosticSanitizer.Sanitize(ex.Message, 500), retryAfterAt: ex.RetryAfterAt,
                        cancellationToken: CancellationToken.None))
                        throw new InvalidOperationException("The rate-limited provider request ledger attempt could not be finalized.", ex);
                    await MarkProviderStepFailedAsync(scope.ServiceProvider, providerExecutions,
                        executionStep.BasicId, run.BasicId, leaseToken, activeFencingVersion,
                        "provider_rate_limited", "Provider rate limit was reached.", CancellationToken.None);
                    failures++;
                    AddBoundedWarning(warnings, $"{providerCode}: provider rate limit (HTTP {ex.HttpStatus}); retry after {ex.RetryAfterAt?.ToString("O") ?? "provider cooldown"}.");
                    Logger.LogWarning("Prospecting provider rate limited for Run {RunId}, provider {Provider}, HTTP {StatusCode}", run.BasicId, providerCode, ex.HttpStatus);
                    continue;
                }
                catch (Exception ex)
                {
                    providerTimer.Stop();
                    // Isolate provider failures only. Ledger write failures remain fatal and are
                    // intentionally outside any nested provider exception handler.
                    if (!await requestLedger.TryFinalizeAsync(
                        attempt.BasicId, "Failed", DateTimeOffset.UtcNow, providerTimer.ElapsedMilliseconds,
                        httpStatus: ex is HttpRequestException httpFailure && httpFailure.StatusCode.HasValue
                            ? (int)httpFailure.StatusCode.Value : null,
                        failureCode: ex is HttpRequestException ? "provider_http_error" : "provider_exception",
                        failureSummary: $"Provider invocation failed ({ex.GetType().Name}).",
                        cancellationToken: CancellationToken.None))
                        throw new InvalidOperationException("The failed provider request ledger attempt could not be finalized.", ex);
                    await MarkProviderStepFailedAsync(scope.ServiceProvider, providerExecutions,
                        executionStep.BasicId, run.BasicId, leaseToken, activeFencingVersion,
                        "provider_invocation_failed", "Provider invocation failed.", CancellationToken.None);
                    failures++;
                    // Exception messages can echo request URLs, query credentials or remote payloads.
                    // Keep persisted Run warnings to a stable classification, not untrusted text.
                    AddBoundedWarning(warnings, $"{providerCode}: {ex.GetType().Name}: provider invocation failed.");
                    Logger.LogWarning("Prospecting provider failed for Run {RunId}, provider {Provider}, failure type {FailureType}", run.BasicId, providerCode, ex.GetType().Name);
                    continue;
                }

                providerTimer.Stop();
                if (result is null || result.Records is null)
                {
                    if (!await requestLedger.TryFinalizeAsync(
                        attempt.BasicId, "Failed", DateTimeOffset.UtcNow, providerTimer.ElapsedMilliseconds,
                        failureCode: "malformed_result", failureSummary: "Provider returned an empty or malformed result envelope.",
                        cancellationToken: CancellationToken.None))
                        throw new InvalidOperationException("The malformed provider request ledger attempt could not be finalized.");
                    await MarkProviderStepFailedAsync(scope.ServiceProvider, providerExecutions,
                        executionStep.BasicId, run.BasicId, leaseToken, activeFencingVersion,
                        "malformed_result", "Provider returned an empty or malformed result envelope.", CancellationToken.None);
                    failures++;
                    AddBoundedWarning(warnings, $"{providerCode}: provider returned an empty or malformed result envelope.");
                    continue;
                }

                var remainingRawRecords = Math.Max(0, maxRawRecordsPerRun - raw.Count);
                var checkpointRecords = new List<CrmProspectingRawRecord>();
                var checkpointProjectedBytes = estimatedRawJsonBytes;
                foreach (var checkpointRecord in result.Records)
                {
                    if (checkpointRecord is null ||
                        !IsWithinRawRecordLimits(checkpointRecord, out var checkpointRecordBytes) ||
                        checkpointRecordBytes > maxRawRecordJsonBytes ||
                        checkpointRecords.Count >= remainingRawRecords ||
                        checkpointProjectedBytes + checkpointRecordBytes + 1 > maxRawJsonBytes)
                        continue;
                    checkpointRecords.Add(checkpointRecord);
                    checkpointProjectedBytes += checkpointRecordBytes + 1;
                }
                var checkpointFailureCount = Math.Max(0, result.FailureCount) +
                    Math.Max(0, result.Records.Count - checkpointRecords.Count);
                var checkpointPayload = new CrmProspectingProviderResultCheckpoint(
                    checkpointRecords, result.IsComplete, checkpointFailureCount);
                var checkpointJson = JsonSerializer.Serialize(checkpointPayload);
                var checkpointBytes = System.Text.Encoding.UTF8.GetByteCount(checkpointJson);
                const int maxProviderCheckpointBytes = 1_048_576;
                if (checkpointBytes > maxProviderCheckpointBytes)
                {
                    if (!await requestLedger.TryFinalizeAsync(
                        attempt.BasicId, "Failed", DateTimeOffset.UtcNow, providerTimer.ElapsedMilliseconds,
                        resultCount: result.Records.Count,
                        failureCode: "provider_checkpoint_oversized",
                        failureSummary: "Normalized provider result exceeded the 1 MiB durable checkpoint limit.",
                        cancellationToken: CancellationToken.None))
                        throw new InvalidOperationException("The oversized provider response request ledger attempt could not be finalized.");
                    if (!await providerExecutions.TryMarkInterruptedNeedsReviewAsync(
                        executionStep.BasicId, run.BasicId, leaseToken, activeFencingVersion,
                        "Started", "provider_checkpoint_oversized",
                        "Normalized provider response exceeded the 1 MiB durable checkpoint limit and requires manual review.",
                        CancellationToken.None))
                        throw new InvalidOperationException("The oversized provider response could not be moved to NeedsReview.");
                    failures++;
                    AddBoundedWarning(warnings, $"{providerCode}: response exceeded the durable checkpoint limit and requires review.");
                    continue;
                }

                if (!await providerExecutions.TrySaveCheckpointAsync(
                    executionStep.BasicId, run.BasicId, leaseToken, activeFencingVersion,
                    checkpointJson, checkpointRecords.Count, cancellationToken))
                    throw new InvalidOperationException($"Provider response checkpoint for '{providerCode}' could not be persisted under the live Run lease.");
                checkpointedSteps.Add((executionStep.BasicId, "Checkpointed",
                    checkpointFailureCount, result.IsComplete, checkpointRecords.Count));

                var providerCanAdvanceCoverage = result.IsComplete && result.FailureCount == 0 &&
                    result.Records.Count <= remainingRawRecords;
                var projectedRawBytes = estimatedRawJsonBytes;
                if (providerCanAdvanceCoverage)
                {
                    foreach (var record in result.Records)
                    {
                        if (record is null ||
                            !IsWithinRawRecordLimits(record, out var estimatedRecordBytes) ||
                            estimatedRecordBytes > maxRawRecordJsonBytes ||
                            projectedRawBytes + estimatedRecordBytes + 1 > maxRawJsonBytes)
                        {
                            providerCanAdvanceCoverage = false;
                            break;
                        }
                        projectedRawBytes += estimatedRecordBytes + 1;
                    }
                }

                var responseHash = CrmProspectingResponseFingerprint.Create(result.Records);
                if (providerCanAdvanceCoverage)
                {
                    // Keep the attempt Started until every accepted record has been durably
                    // reconciled into Candidate/Evidence/Score. Coverage is committed after
                    // candidate processing below, never ahead of durable prospect persistence.
                    pendingCoverage.Add((
                        attempt.BasicId,
                        requestKey,
                        result.Records.Count,
                        responseHash,
                        providerTimer.ElapsedMilliseconds));
                }
                else
                {
                    var incompleteReason = !result.IsComplete
                        ? "Provider indicated that additional result pages remain unread."
                        : result.FailureCount > 0
                            ? "Provider reported one or more record-level failures."
                            : result.Records.Count > remainingRawRecords
                                ? "Provider result exceeded the remaining per-Run retention limit."
                                : "Provider returned one or more invalid or oversized records.";
                    if (!await requestLedger.TryFinalizeAsync(
                        attempt.BasicId, "Failed", DateTimeOffset.UtcNow, providerTimer.ElapsedMilliseconds,
                        resultCount: result.Records.Count,
                        failureCode: !result.IsComplete ? "provider_scan_incomplete" : result.FailureCount > 0 ? "provider_partial_result" : "provider_result_truncated",
                        failureSummary: incompleteReason,
                        responseHash: responseHash,
                        cancellationToken: CancellationToken.None))
                        throw new InvalidOperationException("The incomplete provider request ledger attempt could not be finalized.");
                    if (result.Records.Count > remainingRawRecords)
                    {
                        failures++;
                        AddBoundedWarning(warnings, $"{providerCode}: provider results exceeded the remaining Run retention limit; coverage was not advanced.");
                    }
                }

                failures += Math.Max(0, result.FailureCount);
                if (!result.IsComplete) failures++;
                AddBoundedProviderWarnings(warnings, result.Warnings);
                var remaining = remainingRawRecords;
                foreach (var record in result.Records.Take(remaining))
                {
                    if (record is null)
                    {
                        failures++;
                        AddBoundedWarning(warnings, $"{providerCode}: provider returned a null record; it was skipped.");
                        continue;
                    }

                    if (!IsWithinRawRecordLimits(record, out var estimatedRecordBytes) ||
                        estimatedRecordBytes > maxRawRecordJsonBytes ||
                        estimatedRawJsonBytes + estimatedRecordBytes + 1 > maxRawJsonBytes)
                    {
                        failures++;
                        AddBoundedWarning(warnings, $"{providerCode}: oversized provider record was rejected before retention.");
                        continue;
                    }

                    raw.Add(new CrmProspectingRawEnvelope(provider.ProviderCode.Trim(), record));
                    estimatedRawJsonBytes += estimatedRecordBytes + 1;
                }
                // Persisted cancellation cannot interrupt an arbitrary provider call, but
                // observe it immediately when that call returns and avoid starting later providers.
                // Keep this database read outside the provider exception handler: a read failure
                // must fail the Run rather than be mistaken for a provider outage.
                var afterProvider = await runs.GetByIdAsync(run.BasicId, cancellationToken);
                if (afterProvider?.Status == CrmProspectingRunStatus.CancelRequested)
                    break;
                await RenewLeaseOrThrowAsync(runs, run.BasicId, leaseToken, activeFencingVersion, cancellationToken);
            }


            var serialized = JsonSerializer.Serialize(raw);
            if (System.Text.Encoding.UTF8.GetByteCount(serialized) > 5_000_000)
                throw new InvalidOperationException("Serialized provider output exceeds the 5 MB Run storage limit.");

            var beforeCandidates = await runs.GetByIdAsync(run.BasicId, cancellationToken);
            var candidates = scope.ServiceProvider.GetRequiredService<ICrmProspectingCandidateRepository>();
            var existingCandidates = await candidates.GetListAsync(candidate => candidate.RunId == run.BasicId, cancellationToken);
            if (beforeCandidates?.Status == CrmProspectingRunStatus.CancelRequested)
            {
                if (!await runs.CancelFencedWithDatabaseClockAsync(run.BasicId, leaseToken, activeFencingVersion, cancellationToken))
                    throw new InvalidOperationException("The Run lease was lost before cancellation could be finalized.");
                return;
            }

            var leadRepository = scope.ServiceProvider.GetRequiredService<ICrmLeadRepository>();
            var duplicateEmails = raw
                .Select(envelope => CrmProspectingCandidateFactory.NormalizeEmail(envelope.Record))
                .Where(value => value is not null)
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var duplicateWebsiteHosts = raw
                .Select(envelope => CrmProspectingCandidateFactory.NormalizeWebsiteHost(envelope.Record))
                .Where(value => value is not null)
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var existingLeadMatches = await leadRepository.GetDuplicateMatchesAsync(
                duplicateEmails, duplicateWebsiteHosts, cancellationToken);
            var externalIdsByProvider = raw
                .GroupBy(envelope => envelope.ProviderCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group
                        .Select(envelope => CrmProspectingRecordIdentity.NormalizeExternalId(envelope.Record))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            var crossRunCandidateMatches = await scope.ServiceProvider
                .GetRequiredService<ICrmProspectingCandidateRepository>()
                .GetCrossRunDuplicateMatchesAsync(
                    run.BasicId,
                    externalIdsByProvider,
                    duplicateEmails,
                    duplicateWebsiteHosts,
                    cancellationToken);
            var crossRunIdentityKeys = crossRunCandidateMatches
                .Select(candidate => (candidate.ProviderCode, candidate.ExternalId))
                .ToHashSet();
            var crossRunCandidateEmails = crossRunCandidateMatches
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.NormalizedEmail))
                .Select(candidate => candidate.NormalizedEmail!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var crossRunCandidateWebsites = crossRunCandidateMatches
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.WebsiteHost))
                .Select(candidate => candidate.WebsiteHost!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Match the database unique index semantics: provider/external IDs are exact identities,
            // not case-insensitive email/domain values.
            // Keep provider/external identity as a typed tuple. Delimiter-concatenated keys can
            // collide when either provider-controlled value contains the delimiter.
            var existingKeys = existingCandidates
                .Select(candidate => (candidate.ProviderCode, candidate.ExternalId))
                .ToHashSet();
            var existingCandidateByKey = existingCandidates
                .ToDictionary(candidate => (candidate.ProviderCode, candidate.ExternalId));
            var evidenceRepository = scope.ServiceProvider.GetRequiredService<ICrmProspectingEvidenceRepository>();
            var candidateCount = existingCandidates.Count;
            var seenCandidateEmails = existingCandidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.NormalizedEmail))
                .Select(candidate => candidate.NormalizedEmail!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seenCandidateWebsites = existingCandidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.WebsiteHost))
                .Select(candidate => candidate.WebsiteHost!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Persisted cancellation is polled at a bounded interval; the host token is still
            // checked for every record. CompleteAsync resolves cancellation arriving after the
            // last poll and gives CancelRequested precedence over success.
            const int cancellationPollInterval = 25;
            var gleifLookups = 0;
            var providerProjectionFailures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var identityResolver = scope.ServiceProvider.GetService<ICrmProspectingIdentityResolver>();
            const int maxGleifLookupsPerRun = 20;
            for (var recordIndex = 0; recordIndex < raw.Count; recordIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (recordIndex % cancellationPollInterval == 0)
                {
                    var runState = await runs.GetByIdAsync(run.BasicId, cancellationToken);
                    if (runState?.Status == CrmProspectingRunStatus.CancelRequested) break;
                    await RenewLeaseOrThrowAsync(runs, run.BasicId, leaseToken, activeFencingVersion, cancellationToken);
                }

                var envelope = raw[recordIndex];
                var record = envelope.Record;
                var providerCode = envelope.ProviderCode.Trim();
                if (string.IsNullOrWhiteSpace(providerCode)) throw new InvalidOperationException("Provider code cannot be empty.");
                var externalId = CrmProspectingRecordIdentity.NormalizeExternalId(record);
                if (providerCode.Length > 80) throw new InvalidOperationException("Provider code exceeds 80 characters.");
                var key = (providerCode, externalId);
                if (existingKeys.Contains(key))
                {
                    // Older Runs may already contain a Candidate created before evidence persistence
                    // was wired. Reconcile missing field evidence on retry instead of treating the
                    // Candidate identity as proof that its child records are complete.
                    if (!existingCandidateByKey.TryGetValue(key, out var existingCandidate))
                    {
                        continue;
                    }

                    // Reconcile old/partial Candidates without changing their review or conversion state.
                    await RepairCandidateProvenanceAsync(
                        scope.ServiceProvider,
                        runs,
                        run.BasicId,
                        leaseToken,
                        activeFencingVersion,
                        evidenceRepository,
                        scope.ServiceProvider.GetRequiredService<ICrmProspectingScoreRepository>(),
                        existingCandidate,
                        envelope,
                        cancellationToken);

                    continue;
                }

                var companyName = (string.IsNullOrWhiteSpace(record.CompanyName) ? record.Title : record.CompanyName)?.Trim();
                if (string.IsNullOrWhiteSpace(companyName))
                {
                    failures++;
                    providerProjectionFailures[providerCode] = providerProjectionFailures.GetValueOrDefault(providerCode) + 1;
                    AddBoundedWarning(warnings, $"Provider {providerCode} returned a record without company name/title.");
                    continue;
                }
                if (companyName.Length > 200) companyName = companyName[..200];

                var email = string.IsNullOrWhiteSpace(record.BusinessEmail) ? null : record.BusinessEmail.Trim();
                if (email?.Length > 254) email = null;
                var normalizedEmail = CrmProspectingCandidateFactory.NormalizeEmail(record);
                var websiteHost = CrmProspectingCandidateFactory.NormalizeWebsiteHost(record);

                var candidateEmailDuplicate = normalizedEmail is not null && seenCandidateEmails.Contains(normalizedEmail);
                var candidateWebsiteDuplicate = websiteHost is not null && seenCandidateWebsites.Contains(websiteHost);
                var crossRunIdentityDuplicate = crossRunIdentityKeys.Contains(key);
                var crossRunEmailDuplicate = normalizedEmail is not null && crossRunCandidateEmails.Contains(normalizedEmail);
                var crossRunWebsiteDuplicate = websiteHost is not null && crossRunCandidateWebsites.Contains(websiteHost);
                var duplicate = existingLeadMatches
                    .Select(lead => new
                    {
                        Lead = lead,
                        EmailMatch = normalizedEmail != null && lead.DuplicateEmailNormalized == normalizedEmail,
                        WebsiteMatch = websiteHost != null && lead.WebsiteHost == websiteHost
                    })
                    .Where(match => match.EmailMatch || match.WebsiteMatch)
                    .OrderByDescending(match => match.EmailMatch)
                    .ThenByDescending(match => match.WebsiteMatch)
                    .FirstOrDefault();

                var duplicateReason = duplicate is not null
                    ? duplicate.EmailMatch && duplicate.WebsiteMatch ? "Email and website match an existing lead"
                    : duplicate.EmailMatch ? "Email matches an existing lead" : "Website domain matches an existing lead"
                    : candidateEmailDuplicate ? "Email matches another candidate in this run"
                    : candidateWebsiteDuplicate ? "Website domain matches another candidate in this run"
                    : crossRunIdentityDuplicate ? "Provider notice identity already exists in an earlier Run"
                    : crossRunEmailDuplicate ? "Email matches a candidate in an earlier Run"
                    : crossRunWebsiteDuplicate ? "Website domain matches a candidate in an earlier Run" : null;
                var isDuplicate = duplicate is not null || candidateEmailDuplicate || candidateWebsiteDuplicate ||
                    crossRunIdentityDuplicate || crossRunEmailDuplicate || crossRunWebsiteDuplicate;
                var candidate = CrmProspectingCandidateFactory.Create(
                    run.BasicId,
                    providerCode,
                    record,
                    externalId,
                    companyName,
                    normalizedEmail,
                    websiteHost,
                    isDuplicate,
                    duplicate?.Lead.BasicId,
                    duplicateReason);
                // Candidate/Evidence/Score persistence is shared with the future async page reconciler.
                SysCrmProspectingCandidate insertedCandidate;
                var candidateWasInserted = true;
                try
                {
                    insertedCandidate = await scope.ServiceProvider
                        .GetRequiredService<CrmProspectingCandidatePersistenceService>()
                        .PersistNewAsync(
                            candidate,
                            envelope,
                            leaseToken,
                            activeFencingVersion,
                            DateTimeOffset.UtcNow,
                            cancellationToken);
                }
                catch (Exception ex) when (IsCandidateIdentityUniqueViolation(ex))
                {
                    // The failed insertion UoW has been disposed before this catch. Read the winner
                    // in the current tenant and repair only missing provenance; never rewrite review state.
                    var winner = await candidates.GetByIdentityAsync(
                        run.BasicId,
                        providerCode,
                        externalId,
                        cancellationToken);
                    if (winner is null) throw;

                    insertedCandidate = winner;
                    candidateWasInserted = false;
                    await RepairCandidateProvenanceAsync(
                        scope.ServiceProvider,
                        runs,
                        run.BasicId,
                        leaseToken,
                        activeFencingVersion,
                        evidenceRepository,
                        scope.ServiceProvider.GetRequiredService<ICrmProspectingScoreRepository>(),
                        insertedCandidate,
                        envelope,
                        cancellationToken);
                }

                // Optional authoritative identity enrichment is performed only after a
                // Candidate exists. Exact normalized legal-name matches only; ambiguity or
                // registry unavailability never blocks procurement evidence or Candidate review.
                CrmProspectingLegalEntityMatch? identity = null;
                if (candidateWasInserted && identityResolver is not null && gleifLookups < maxGleifLookupsPerRun)
                {
                    var identityLedger = scope.ServiceProvider.GetRequiredService<ICrmProspectingProviderRequestRepository>();
                    const string identityProviderCode = "gleif_identity";
                    var identityCooldown = await identityLedger.GetActiveCooldownUntilAsync(
                        identityProviderCode, DateTimeOffset.UtcNow, cancellationToken);
                    if (identityCooldown is DateTimeOffset identityCooldownUntil)
                    {
                        AddBoundedWarning(warnings,
                            $"GLEIF identity lookup skipped until {identityCooldownUntil:O} because its provider cooldown is active.");
                    }
                    else
                    {
                        gleifLookups++;
                        var identityRequest = new CrmProspectingProviderRequest(
                            run.BasicId,
                            "gleif_exact_identity_lookup",
                            insertedCandidate.CompanyName,
                            string.Empty,
                            string.Empty,
                            "{}",
                            Array.Empty<string>());
                        var identityRequestKey = CrmProspectingRequestFingerprint.Create(identityProviderCode, identityRequest);
                        var identityAttempt = await identityLedger.CreateStartedAsync(
                            run.BasicId, identityProviderCode, identityRequestKey, DateTimeOffset.UtcNow, cancellationToken);
                        var identityTimer = System.Diagnostics.Stopwatch.StartNew();
                        var identityAttemptFinalized = false;
                        try
                        {
                            identity = await identityResolver.ResolveExactAsync(insertedCandidate.CompanyName, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            identityTimer.Stop();
                            if (!await identityLedger.TryFinalizeAsync(
                                identityAttempt.BasicId, "Cancelled", DateTimeOffset.UtcNow, identityTimer.ElapsedMilliseconds,
                                failureCode: "operation_cancelled", failureSummary: "GLEIF identity lookup was cancelled.",
                                cancellationToken: CancellationToken.None))
                                throw new InvalidOperationException("Could not finalize the cancelled GLEIF request attempt.");
                            identityAttemptFinalized = true;
                            throw;
                        }
                        catch (CrmProspectingProviderRateLimitException rateLimitException)
                        {
                            identityTimer.Stop();
                            if (!await identityLedger.TryFinalizeAsync(
                                identityAttempt.BasicId, "RateLimited", DateTimeOffset.UtcNow, identityTimer.ElapsedMilliseconds,
                                httpStatus: rateLimitException.HttpStatus,
                                failureCode: "provider_rate_limited",
                                failureSummary: BuildFailureSummary(rateLimitException),
                                retryAfterAt: rateLimitException.RetryAfterAt,
                                cancellationToken: CancellationToken.None))
                                throw new InvalidOperationException("Could not finalize the rate-limited GLEIF request attempt.");
                            identityAttemptFinalized = true;
                            AddBoundedWarning(warnings, $"GLEIF identity lookup for candidate {insertedCandidate.BasicId} was rate limited.");
                            Logger.LogWarning(rateLimitException, "GLEIF identity lookup was rate limited for Candidate {CandidateId}", insertedCandidate.BasicId);
                        }
                        catch (Exception identityException)
                        {
                            identityTimer.Stop();
                            if (!await identityLedger.TryFinalizeAsync(
                                identityAttempt.BasicId, "Failed", DateTimeOffset.UtcNow, identityTimer.ElapsedMilliseconds,
                                failureCode: "identity_lookup_failed",
                                failureSummary: BuildFailureSummary(identityException),
                                cancellationToken: CancellationToken.None))
                                throw new InvalidOperationException("Could not finalize the failed GLEIF request attempt.", identityException);
                            identityAttemptFinalized = true;
                            AddBoundedWarning(warnings, $"GLEIF identity lookup for candidate {insertedCandidate.BasicId} failed: {identityException.GetType().Name}.");
                            Logger.LogWarning(identityException, "GLEIF identity lookup failed for Candidate {CandidateId}", insertedCandidate.BasicId);
                        }

                        if (!identityAttemptFinalized)
                        {
                            identityTimer.Stop();
                            var responseHash = identity?.RawPayloadHash;
                            if (!await identityLedger.TryFinalizeAsync(
                                identityAttempt.BasicId, "Succeeded", DateTimeOffset.UtcNow, identityTimer.ElapsedMilliseconds,
                                resultCount: identity is null ? 0 : 1,
                                responseHash: responseHash,
                                cancellationToken: CancellationToken.None))
                                throw new InvalidOperationException("Could not finalize the successful GLEIF request attempt.");
                        }
                    }
                }

                    if (identity is not null &&
                        CrmProspectingEvidenceFactory.TryGetCanonicalSourceUrl(identity.SourceUrl, out var canonicalSourceUrl))
                    {
                        var identityEvidence = new SysCrmProspectingEvidence
                        {
                            CandidateId = insertedCandidate.BasicId,
                            ProviderCode = "gleif_lei",
                            ProviderRecordId = identity.Lei,
                            FieldName = "legal_entity_identifier",
                            FieldValueHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity.Lei))),
                            SourceUrl = canonicalSourceUrl,
                            SourceUrlHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonicalSourceUrl))),
                            EvidenceType = "registry",
                            VerificationStatus = "unverified",
                            EvidenceSummary = $"GLEIF exact legal-name match: {identity.LegalName}; entity status: {identity.EntityStatus ?? "unknown"}; LEI registration status: {identity.RegistrationStatus ?? "unknown"}; country: {identity.CountryCode ?? "unknown"}; registered identifier: {identity.RegisteredAs ?? "not supplied"}. Human verification remains required.",
                            RetrievedAtUtc = identity.RetrievedAtUtc.ToUniversalTime(),
                            RawPayloadHash = identity.RawPayloadHash
                        };
                        using var identityUnitOfWork = scope.ServiceProvider
                            .GetRequiredService<IUnitOfWorkManager>()
                            .Begin(new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true);
                        if (!await runs.TryLockLeaseForWriteAsync(run.BasicId, leaseToken, activeFencingVersion, cancellationToken))
                            throw new InvalidOperationException("The Run lease expired or was superseded before registry evidence persistence.");
                        await evidenceRepository.AddRangeAsync([identityEvidence], cancellationToken);
                        await identityUnitOfWork.CompleteAsync(cancellationToken);
                    }
                }

                existingCandidateByKey[key] = insertedCandidate;

                existingKeys.Add(key);
                if (normalizedEmail is not null) seenCandidateEmails.Add(normalizedEmail);
                if (websiteHost is not null) seenCandidateWebsites.Add(websiteHost);
                candidateCount++;
            }

            // All candidate/evidence/score writes above are durable and idempotent by
            // provider identity. Only now advance checkpointed provider steps. If the process
            // stops before this point, the next delivery replays the verified checkpoint.
            var projectionRunState = await runs.GetByIdAsync(run.BasicId, cancellationToken);
            if (projectionRunState?.Status != CrmProspectingRunStatus.CancelRequested)
            {
                foreach (var step in checkpointedSteps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var stepUnitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>()
                        .Begin(new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true);
                    if (step.State == "Checkpointed" &&
                        !await providerExecutions.TryTransitionAsync(
                            step.ExecutionId, run.BasicId, leaseToken, activeFencingVersion,
                            "Checkpointed", "Projecting", cancellationToken: cancellationToken))
                        throw new InvalidOperationException("A provider checkpoint could not enter Projecting under the live Run lease.");
                    var stepProviderCode = persistedPlan.Single(row => row.BasicId == step.ExecutionId).ProviderCode;
                    var projectionFailures = providerProjectionFailures.GetValueOrDefault(stepProviderCode);
                    var stepFailureCount = step.FailureCount + projectionFailures +
                        (!step.IsComplete && step.FailureCount == 0 ? 1 : 0);
                    var stepSucceeded = step.IsComplete && stepFailureCount == 0;
                    var transitioned = stepSucceeded
                        ? await providerExecutions.TryTransitionAsync(
                            step.ExecutionId, run.BasicId, leaseToken, activeFencingVersion,
                            "Projecting", "Succeeded",
                            projectedRecordCount: step.RecordCount, failureCount: 0,
                            cancellationToken: cancellationToken)
                        : await providerExecutions.TryTransitionAsync(
                            step.ExecutionId, run.BasicId, leaseToken, activeFencingVersion,
                            "Projecting", "Failed",
                            projectedRecordCount: step.RecordCount, failureCount: Math.Max(1, stepFailureCount),
                            failureCode: "provider_partial_result",
                            safeFailureSummary: "Provider returned an incomplete result or one or more records could not be projected.",
                            cancellationToken: cancellationToken);
                    if (!transitioned)
                        throw new InvalidOperationException($"Projected provider checkpoint '{stepProviderCode}' could not reach its terminal state.");
                    await stepUnitOfWork.CompleteAsync(cancellationToken);
                }
            }

            // Incremental scan coverage must never get ahead of durable Candidate/Evidence/Score
            // writes. If the process crashes before this point, the Started ledger attempts are
            // recoverable and the old checkpoint causes a safe overlapping rescan. If cancellation
            // was requested, finalize attempts as cancelled and leave coverage unchanged.
            var finalRunState = await runs.GetByIdAsync(run.BasicId, cancellationToken);
            if (finalRunState?.Status == CrmProspectingRunStatus.CancelRequested)
            {
                foreach (var pending in pendingCoverage)
                {
                    if (!await requestLedger.TryFinalizeAsync(
                        pending.AttemptId, "Cancelled", DateTimeOffset.UtcNow, pending.DurationMs,
                        resultCount: pending.ResultCount,
                        failureCode: "run_cancel_requested",
                        failureSummary: "The Run was cancelled before provider coverage could be committed.",
                        responseHash: pending.ResponseHash,
                        cancellationToken: CancellationToken.None))
                        throw new InvalidOperationException("A cancelled provider request ledger attempt could not be finalized.");
                }
            }
            else
            {
                foreach (var pending in pendingCoverage)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await RenewLeaseOrThrowAsync(runs, run.BasicId, leaseToken, activeFencingVersion, cancellationToken);
                    var finalizedWithCoverage = await requestLedger.TryFinalizeSuccessWithCoverageAsync(
                        pending.AttemptId, run.BasicId, run.CampaignId, leaseToken, activeFencingVersion,
                        pending.RequestKey, DateTimeOffset.UtcNow, pending.DurationMs,
                        pending.ResultCount, pending.ResponseHash, CancellationToken.None);
                    if (!finalizedWithCoverage)
                    {
                        var latestRun = await runs.GetByIdAsync(run.BasicId, CancellationToken.None);
                        if (latestRun?.Status == CrmProspectingRunStatus.CancelRequested)
                        {
                            if (!await requestLedger.TryFinalizeAsync(
                                pending.AttemptId, "Cancelled", DateTimeOffset.UtcNow, pending.DurationMs,
                                resultCount: pending.ResultCount,
                                failureCode: "run_cancel_requested",
                                failureSummary: "The Run was cancelled before provider coverage could be committed.",
                                responseHash: pending.ResponseHash,
                                cancellationToken: CancellationToken.None))
                                throw new InvalidOperationException("The cancelled provider request ledger attempt could not be finalized.");
                            break;
                        }
                        throw new InvalidOperationException("The complete provider request could not atomically finalize and advance coverage after candidate persistence.");
                    }
                }
            }

            var summary = warnings.Count == 0 ? null : string.Join(" | ", warnings).Trim();
            if (summary?.Length > 2000) summary = summary[..2000];

            finalRunState = await runs.GetByIdAsync(run.BasicId, cancellationToken);
            var finalized = finalRunState?.Status == CrmProspectingRunStatus.CancelRequested
                ? await runs.CancelFencedWithDatabaseClockAsync(run.BasicId, leaseToken, activeFencingVersion, cancellationToken)
                : await runs.CompleteFencedWithDatabaseClockAsync(
                    run.BasicId, leaseToken, activeFencingVersion, serialized, raw.Count, failures,
                    summary, cancellationToken, candidateCount);
            // Cancellation may race the final read and Complete UPDATE. If Complete loses
            // because the status became CancelRequested, resolve it through the same live fence.
            if (!finalized)
                finalized = await runs.CancelFencedWithDatabaseClockAsync(
                    run.BasicId, leaseToken, activeFencingVersion, cancellationToken);
            if (!finalized)
                throw new InvalidOperationException("The Run lease was lost before its terminal transition.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave Running persisted; controlled single-worker startup recovery requeues it after a process stop.
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Prospecting Run processing failed: {RunId}", message.RunId);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(message.TenantId);
                if (activeLeaseToken is Guid failureLeaseToken && activeFencingVersion > 0)
                    await scope.ServiceProvider.GetRequiredService<ICrmProspectingRunRepository>()
                        .FailFencedWithDatabaseClockAsync(
                            message.RunId, failureLeaseToken, activeFencingVersion, BuildFailureSummary(ex), cancellationToken);
            }
            catch (Exception persistException)
            {
                Logger.LogError(persistException, "Could not persist prospecting Run failure: {RunId}", message.RunId);
            }
        }
        finally
        {
            if (leaseHeartbeatCts is not null)
            {
                leaseHeartbeatCts.Cancel();
                if (leaseHeartbeatTask is not null)
                {
                    try { await leaseHeartbeatTask; }
                    catch (OperationCanceledException) { }
                }
                leaseHeartbeatCts.Dispose();
            }
        }
    }

    private async Task RunLeaseHeartbeatAsync(
        long tenantId,
        long runId,
        Guid leaseToken,
        long fencingVersion,
        CancellationTokenSource runCancellation)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        try
        {
            while (await timer.WaitForNextTickAsync(runCancellation.Token))
            {
                using var scope = _scopeFactory.CreateScope();
                using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId);
                var runs = scope.ServiceProvider.GetRequiredService<ICrmProspectingRunRepository>();
                var current = await runs.GetByIdAsync(runId, runCancellation.Token);
                // CancelRequested intentionally stops lease renewal without cancelling the
                // worker token: the processing path must observe it and finalize via the
                // fenced Cancel transition rather than treating user cancellation as lease loss.
                if (current?.Status == CrmProspectingRunStatus.CancelRequested)
                    return;
                if (current?.Status != CrmProspectingRunStatus.Running)
                    throw new InvalidOperationException("The prospecting Run heartbeat found a non-running or missing Run.");
                if (!await runs.RenewLeaseWithDatabaseClockAsync(
                    runId, leaseToken, fencingVersion, RunLeaseDuration, runCancellation.Token))
                    throw new InvalidOperationException("The prospecting Run heartbeat detected an expired or superseded lease.");
            }
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            // Normal shutdown, Run completion, or lease-loss cancellation.
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Prospecting Run lease heartbeat failed; cancelling Run {RunId}", runId);
            runCancellation.Cancel();
        }
    }

    private static bool IsScoreIdempotencyUniqueViolation(Exception exception)
    {
        // Initial score snapshots are deduplicated by the exact tenant-scoped unique index.
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var type = current.GetType();
            if (!string.Equals(type.FullName, "Npgsql.PostgresException", StringComparison.Ordinal))
                continue;

            var sqlState = type.GetProperty("SqlState")?.GetValue(current) as string;
            var constraintName = type.GetProperty("ConstraintName")?.GetValue(current) as string;
            return string.Equals(sqlState, "23505", StringComparison.Ordinal)
                && string.Equals(constraintName, "ux_crm_prospecting_score_te_idemkey", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsEvidenceIdentityUniqueViolation(Exception exception)
    {
        // Only the exact immutable Evidence identity index is eligible for bounded reconciliation.
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var type = current.GetType();
            if (!string.Equals(type.FullName, "Npgsql.PostgresException", StringComparison.Ordinal))
                continue;

            var sqlState = type.GetProperty("SqlState")?.GetValue(current) as string;
            var constraintName = type.GetProperty("ConstraintName")?.GetValue(current) as string;
            return string.Equals(sqlState, "23505", StringComparison.Ordinal)
                && string.Equals(constraintName, "ux_crm_prospecting_evidence_evidenceidentity", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsCandidateIdentityUniqueViolation(Exception exception)
    {
        // Deliberately fail closed for other providers and unrelated unique constraints.
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var type = current.GetType();
            if (!string.Equals(type.FullName, "Npgsql.PostgresException", StringComparison.Ordinal))
                continue;

            var sqlState = type.GetProperty("SqlState")?.GetValue(current) as string;
            var constraintName = type.GetProperty("ConstraintName")?.GetValue(current) as string;
            return string.Equals(sqlState, "23505", StringComparison.Ordinal)
                && string.Equals(constraintName, "ux_crm_prospecting_candidate_te_ru_pr_ex_is", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static async Task RepairCandidateProvenanceAsync(
        IServiceProvider serviceProvider,
        ICrmProspectingRunRepository runs,
        long runId,
        Guid leaseToken,
        long fencingVersion,
        ICrmProspectingEvidenceRepository evidenceRepository,
        ICrmProspectingScoreRepository scoreRepository,
        SysCrmProspectingCandidate candidate,
        CrmProspectingRawEnvelope envelope,
        CancellationToken cancellationToken,
        int repairAttempt = 0)
    {
        var expectedEvidence = CrmProspectingEvidenceFactory.Create(
            candidate.BasicId,
            envelope,
            DateTimeOffset.UtcNow);
        var persistedEvidence = await evidenceRepository.GetByCandidateAsync(
            candidate.BasicId,
            cancellationToken);
        var persistedEvidenceKeys = persistedEvidence
            .Select(evidence => (
                evidence.ProviderCode,
                evidence.ProviderRecordId,
                evidence.FieldName,
                evidence.SourceUrlHash,
                evidence.RawPayloadHash))
            .ToHashSet();
        var missingEvidence = expectedEvidence
            .Where(evidence => !persistedEvidenceKeys.Contains((
                evidence.ProviderCode,
                evidence.ProviderRecordId,
                evidence.FieldName,
                evidence.SourceUrlHash,
                evidence.RawPayloadHash)))
            .ToArray();

        var existingScores = await scoreRepository.GetLatestByCandidateAsync(
            candidate.BasicId, 1, cancellationToken);
        var needsScoreRepair = existingScores.Count == 0;
        if (missingEvidence.Length == 0 && !needsScoreRepair) return;

        // Evidence repair and the first missing Score snapshot remain atomic.
        // Candidate review/conversion fields are intentionally never updated here.
        try
        {
            using var repairUnitOfWork = serviceProvider
                .GetRequiredService<IUnitOfWorkManager>()
                .Begin(new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            if (!await runs.TryLockLeaseForWriteAsync(runId, leaseToken, fencingVersion, cancellationToken))
                throw new InvalidOperationException("The Run lease expired or was superseded before provenance repair.");

            if (missingEvidence.Length > 0)
            {
                await evidenceRepository.AddRangeAsync(missingEvidence, cancellationToken);
            }

            if (needsScoreRepair)
            {
                var completeEvidence = persistedEvidence.Concat(missingEvidence).ToArray();
                var scoreEvidence = completeEvidence
                    .OrderByDescending(evidence =>
                        (evidence.EvidenceType is "direct" or "registry") &&
                        !string.IsNullOrWhiteSpace(evidence.SourceUrlHash))
                    .ThenBy(evidence => evidence.ProviderCode, StringComparer.Ordinal)
                    .ThenBy(evidence => evidence.ProviderRecordId, StringComparer.Ordinal)
                    .ThenBy(evidence => evidence.FieldName, StringComparer.Ordinal)
                    .ThenBy(evidence => evidence.SourceUrlHash, StringComparer.Ordinal)
                    .ThenBy(evidence => evidence.RawPayloadHash, StringComparer.Ordinal)
                    .Take(32)
                    .ToArray();
                var evaluatedAtUtc = DateTimeOffset.UtcNow;
                var scoreInput = new CrmProspectingScoreInput(
                    HasVerifiableOrganization: false,
                    HasDirectProcurementSignal: false,
                    HasDirectProductFit: false,
                    HasTargetMarketFit: false,
                    PublishedAtUtc: completeEvidence
                        .Where(evidence => evidence.PublishedAtUtc.HasValue)
                        .Select(evidence => evidence.PublishedAtUtc)
                        .Max(),
                    HasTraceablePrimaryEvidence: completeEvidence.Any(evidence =>
                        (evidence.EvidenceType is "direct" or "registry") &&
                        evidence.VerificationStatus == "unverified" &&
                        !string.IsNullOrWhiteSpace(evidence.SourceUrlHash)),
                    HasPublicBusinessContact: !string.IsNullOrWhiteSpace(candidate.BusinessEmail),
                    IsKnownDuplicate: candidate.Status == CrmProspectingCandidateStatus.Duplicate,
                    IsRestrictedOrUnsafeSource: false);
                var scoreResult = CrmProspectingScorecard.Evaluate(scoreInput, evaluatedAtUtc);
                var scoreSnapshot = CrmProspectingScoreSnapshotFactory.Create(
                    candidate.BasicId, scoreResult, scoreEvidence, BuildInitialScoreIdempotencyKey(candidate.BasicId, scoreResult.ScorecardVersion));
                await scoreRepository.AddAsync(scoreSnapshot, cancellationToken);
            }

            await repairUnitOfWork.CompleteAsync(cancellationToken);
        }
        catch (Exception ex) when (repairAttempt == 0 && (IsEvidenceIdentityUniqueViolation(ex) || IsScoreIdempotencyUniqueViolation(ex)))
        {
            // The failed repair UoW has been disposed. Confirm the exact score winner before
            // retrying a score-key collision; evidence collisions are reconciled by full identity.
            if (IsScoreIdempotencyUniqueViolation(ex))
            {
                var scoreKey = BuildInitialScoreIdempotencyKey(candidate.BasicId, CrmProspectingScorecard.Version);
                var winningScore = await scoreRepository.GetByIdempotencyKeyAsync(scoreKey, cancellationToken);
                if (winningScore is null) throw;
            }

            // Retry once; unrelated DB errors and repeated collisions fail closed.
            await RepairCandidateProvenanceAsync(
                serviceProvider,
                runs,
                runId,
                leaseToken,
                fencingVersion,
                evidenceRepository,
                scoreRepository,
                candidate,
                envelope,
                cancellationToken,
                repairAttempt: 1);
        }
    }

    private static async Task RenewLeaseOrThrowAsync(
        ICrmProspectingRunRepository runs,
        long runId,
        Guid leaseToken,
        long fencingVersion,
        CancellationToken cancellationToken)
    {
        if (!await runs.RenewLeaseWithDatabaseClockAsync(
            runId, leaseToken, fencingVersion, RunLeaseDuration, cancellationToken))
            throw new InvalidOperationException("The prospecting Run lease expired or was superseded.");
    }

    private static string BuildInitialScoreIdempotencyKey(long candidateId, string scorecardVersion)
    {
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));
        if (string.IsNullOrWhiteSpace(scorecardVersion) || scorecardVersion.Length > 80)
            throw new ArgumentException("Scorecard version must contain 1-80 characters.", nameof(scorecardVersion));
        return $"initial:{candidateId}:{scorecardVersion}";
    }

    private const int MaxFailureSummaryLength = 2000;

    private static string BuildFailureSummary(Exception exception)
    {
        var summary = $"{exception.GetType().Name}: {exception.Message}";
        return SanitizeDiagnosticText(summary, MaxFailureSummaryLength);
    }

    private static string SanitizeDiagnosticText(string? value, int maxLength)
        => CrmProspectingDiagnosticSanitizer.Sanitize(value, maxLength);

    private const int MaxAggregatedWarnings = 100;
    private const int MaxAggregatedWarningLength = 500;

    private static void AddBoundedWarning(List<string> warnings, string? warning)
    {
        if (warnings.Count >= MaxAggregatedWarnings || string.IsNullOrWhiteSpace(warning)) return;
        // Provider output and exception messages are untrusted; strip control characters
        // before persisting them into the Run summary or exposing them in review screens.
        var value = SanitizeDiagnosticText(warning, MaxAggregatedWarningLength);
        if (value.Length == 0) return;
        warnings.Add(value);
    }

    private static void AddBoundedProviderWarnings(List<string> warnings, IReadOnlyList<string>? providerWarnings)
    {
        if (providerWarnings is null || warnings.Count >= MaxAggregatedWarnings) return;

        foreach (var warning in providerWarnings)
        {
            if (warnings.Count >= MaxAggregatedWarnings) break;
            AddBoundedWarning(warnings, warning);
        }
    }

    private static bool IsWithinRawRecordLimits(CrmProspectingRawRecord record, out long estimatedJsonBytes)
    {
        estimatedJsonBytes = 128;
        static bool Fits(string? value, int maxLength) => value is null || value.Length <= maxLength;
        if (!Fits(record.ExternalId, 4096) ||
            !Fits(record.Source, 200) ||
            !Fits(record.Title, 300) ||
            !Fits(record.CompanyName, 200) ||
            !Fits(record.Website, 500) ||
            !Fits(record.ContactName, 200) ||
            !Fits(record.BusinessEmail, 254) ||
            !Fits(record.Summary, 5000) ||
            !Fits(record.SourceUrl, 2000) ||
            !Fits(record.RawPayloadHash, 64) ||
            !Fits(record.EvidenceType, 40))
            return false;

        // Six bytes per UTF-16 code unit safely covers JSON escaping of control characters,
        // plus a fixed allowance for property names, quotes, separators and timestamp fields.
        var characters = (long)(record.ExternalId?.Length ?? 0) +
            (record.Source?.Length ?? 0) +
            (record.Title?.Length ?? 0) +
            (record.CompanyName?.Length ?? 0) +
            (record.Website?.Length ?? 0) +
            (record.ContactName?.Length ?? 0) +
            (record.BusinessEmail?.Length ?? 0) +
            (record.Summary?.Length ?? 0) +
            (record.SourceUrl?.Length ?? 0) +
            (record.RawPayloadHash?.Length ?? 0) +
            (record.EvidenceType?.Length ?? 0);
        estimatedJsonBytes += characters * 6;
        return true;
    }



    private static async Task MarkProviderStepFailedAsync(
        IServiceProvider serviceProvider,
        ICrmProspectingRunProviderExecutionRepository repository,
        long executionId,
        long runId,
        Guid leaseToken,
        long fencingVersion,
        string failureCode,
        string safeFailureSummary,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = serviceProvider.GetRequiredService<IUnitOfWorkManager>()
            .Begin(new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        if (!await repository.TryTransitionAsync(
            executionId, runId, leaseToken, fencingVersion,
            "Started", "Failed",
            failureCount: 1,
            failureCode: failureCode,
            safeFailureSummary: safeFailureSummary,
            cancellationToken: cancellationToken))
            throw new InvalidOperationException("The failed provider execution step could not be finalized under the live Run lease.");
        await unitOfWork.CompleteAsync(cancellationToken);
    }


    private static IDynamicServiceConfig BuildConfig(IOptions<XiHanBackgroundServiceOptions> options)
    {
        var config = new DynamicServiceConfig(options);
        config.UpdateMaxConcurrentTasks(1);
        config.UpdateIdleDelay(3000);
        return config;
    }

    private async Task RecoverPendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runs = scope.ServiceProvider.GetRequiredService<ICrmProspectingRunRepository>();
            var runner = scope.ServiceProvider.GetRequiredService<ITenantDataScopeRunner>();
            var queued = 0;

            await runner.RunAsync(async tenantId =>
            {
                // Entity CodeFirst does not emit PostgreSQL CHECK constraints. Apply the same
                // invariants as migration 5.9.31 before recovery or any worker writes.
                var requestLedger = scope.ServiceProvider.GetRequiredService<ICrmProspectingProviderRequestRepository>();
                await requestLedger.EnsureSchemaConstraintsAsync(cancellationToken);

                // Recover lease-backed work first using PostgreSQL's clock. This is safe to run
                // at startup even while the legacy single-worker path remains active: only expired
                // rows carrying lease metadata are touched by this operation.
                await runs.RecoverExpiredLeasesAsync(cancellationToken);
                await runs.RecoverInterruptedAsync(DateTimeOffset.UtcNow, cancellationToken);
                var interruptedRequests = await requestLedger.RecoverStaleStartedAsync(
                    RunLeaseDuration, cancellationToken);
                if (interruptedRequests > 0)
                    Logger.LogInformation("Recovered {Count} interrupted prospecting provider requests", interruptedRequests);
                var ids = await runs.GetQueuedIdsAsync(cancellationToken);
                foreach (var id in ids)
                {
                    await _queue.EnqueueAsync(
                        new CrmProspectingRunMessage
                        {
                            RunId = id,
                            TenantId = tenantId ?? throw new InvalidOperationException(
                                "Prospecting startup recovery requires an explicit tenant context."),
                            CreatedAt = DateTimeOffset.UtcNow
                        },
                        TimeSpan.Zero,
                        cancellationToken);
                }

                queued += ids.Count;
            }, cancellationToken);

            if (queued > 0) Logger.LogInformation("Prospecting startup recovery requeued {Count} runs", queued);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Prospecting startup recovery failed; worker will continue polling");
        }
    }

    private static IReadOnlyList<string> ParseStringArray(string? json, string label, bool allowNull = false)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            if (allowNull) return [];
            throw new InvalidOperationException($"The {label} JSON array is required.");
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"The {label} must be a JSON array.");

        var values = new List<string>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException($"Every {label} entry must be a string.");
            var value = element.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }

        return values;
    }
}
