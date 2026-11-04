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
        try
        {
            using var scope = _scopeFactory.CreateScope();
            using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(message.TenantId);

            var runs = scope.ServiceProvider.GetRequiredService<ICrmProspectingRunRepository>();
            var run = await runs.TryClaimLeaseAsync(message.RunId, LeaseOwner, RunLeaseDuration, cancellationToken);
            if (run is null) return;
            if (run.LeaseToken is not Guid leaseToken || leaseToken == Guid.Empty || run.FencingVersion <= 0)
                throw new InvalidOperationException("The claimed Run did not return a valid lease token and fencing version.");
            activeLeaseToken = leaseToken;
            activeFencingVersion = run.FencingVersion;

            // Provider implementations are extension points; ignore malformed registrations
            // rather than letting a null/blank/oversized key crash the entire Run before dispatch.
            var providers = scope.ServiceProvider.GetServices<ICrmProspectingProvider>()
                .Where(provider => !string.IsNullOrWhiteSpace(provider.ProviderCode) && provider.ProviderCode.Trim().Length <= 80)
                .GroupBy(provider => provider.ProviderCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var plan = ParseStringArray(run.SnapshotProviderPlanJson, "provider plan");
            if (plan.Count == 0) throw new InvalidOperationException("The provider plan contains no usable provider codes.");
            var excludedDomains = ParseStringArray(run.SnapshotExcludedDomainsJson, "excluded domains", allowNull: true);
            var raw = new List<CrmProspectingRawEnvelope>();
            long estimatedRawJsonBytes = 2; // JSON array delimiters
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

                if (!providers.TryGetValue(providerCode, out var provider))
                {
                    failures++;
                    AddBoundedWarning(warnings, $"Provider is not registered: {providerCode}");
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
                CrmProspectingProviderResult result;
                try
                {
                    result = await provider.SearchAsync(request, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Isolate provider failures only. Database/state/persistence failures below
                    // must fail the Run instead of being misreported as a provider outage.
                    failures++;
                    AddBoundedWarning(warnings, $"{providerCode}: {ex.GetType().Name}: {ex.Message}");
                    Logger.LogWarning(ex, "Prospecting provider failed for Run {RunId}, provider {Provider}", run.BasicId, providerCode);
                    continue;
                }

                if (result is null || result.Records is null)
                {
                    failures++;
                    AddBoundedWarning(warnings, $"{providerCode}: provider returned an empty or malformed result envelope.");
                    continue;
                }

                failures += Math.Max(0, result.FailureCount);
                AddBoundedProviderWarnings(warnings, result.Warnings);
                const int maxRawRecordsPerRun = 2000;
                var remaining = Math.Max(0, maxRawRecordsPerRun - raw.Count);
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
                if (result.Records.Count > remaining)
                {
                    failures++;
                    AddBoundedWarning(warnings, $"Raw result cap reached; {result.Records.Count - remaining} provider records were not retained.");
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
                .Select(envelope => NormalizeCandidateEmail(envelope.Record))
                .Where(value => value is not null)
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var duplicateWebsiteHosts = raw
                .Select(envelope => NormalizeCandidateWebsiteHost(envelope.Record))
                .Where(value => value is not null)
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var existingLeadMatches = await leadRepository.GetDuplicateMatchesAsync(
                duplicateEmails, duplicateWebsiteHosts, cancellationToken);
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
                    AddBoundedWarning(warnings, $"Provider {providerCode} returned a record without company name/title.");
                    continue;
                }
                if (companyName.Length > 200) companyName = companyName[..200];

                var email = string.IsNullOrWhiteSpace(record.BusinessEmail) ? null : record.BusinessEmail.Trim();
                if (email?.Length > 254) email = null;
                var normalizedEmail = CrmLeadDuplicateMatcher.NormalizeEmail(email);
                var website = string.IsNullOrWhiteSpace(record.Website) ? null : record.Website.Trim();
                if (website?.Length > 500) website = website[..500];
                var websiteHost = CrmLeadDuplicateMatcher.NormalizeDomain(website);

                var candidateEmailDuplicate = normalizedEmail is not null && seenCandidateEmails.Contains(normalizedEmail);
                var candidateWebsiteDuplicate = websiteHost is not null && seenCandidateWebsites.Contains(websiteHost);
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

                var source = record.Source?.Trim();
                var title = record.Title?.Trim();
                var contactName = record.ContactName?.Trim();
                var summary = record.Summary?.Trim();
                var candidate = new XiHan.BasicApp.Crm.Domain.Entities.SysCrmProspectingCandidate
                {
                    RunId = run.BasicId,
                    ProviderCode = providerCode,
                    ExternalId = externalId,
                    Source = string.IsNullOrWhiteSpace(source) ? null : source[..Math.Min(source.Length, 200)],
                    Title = string.IsNullOrWhiteSpace(title) ? null : title[..Math.Min(title.Length, 300)],
                    CompanyName = companyName,
                    ContactName = string.IsNullOrWhiteSpace(contactName) ? null : contactName[..Math.Min(contactName.Length, 200)],
                    BusinessEmail = email,
                    NormalizedEmail = normalizedEmail,
                    Website = website,
                    WebsiteHost = websiteHost,
                    Summary = string.IsNullOrWhiteSpace(summary) ? null : summary[..Math.Min(summary.Length, 5000)],
                    Status = duplicate is null && !candidateEmailDuplicate && !candidateWebsiteDuplicate
                        ? CrmProspectingCandidateStatus.PendingReview
                        : CrmProspectingCandidateStatus.Duplicate,
                    DuplicateLeadId = duplicate?.Lead.BasicId,
                    DuplicateReason = duplicate is not null
                        ? duplicate.EmailMatch && duplicate.WebsiteMatch ? "Email and website match an existing lead"
                        : duplicate.EmailMatch ? "Email matches an existing lead" : "Website domain matches an existing lead"
                        : candidateEmailDuplicate ? "Email matches another candidate in this run"
                        : candidateWebsiteDuplicate ? "Website domain matches another candidate in this run" : null
                };
                // The background worker is not an HTTP request and must not rely on MVC/AOP
                // UnitOfWork interception. Use an explicit short transaction for the candidate
                // and its provenance rows; provider network calls stay outside this transaction.
                SysCrmProspectingCandidate insertedCandidate;
                var candidateWasInserted = true;
                try
                {
                    using (var unitOfWork = scope.ServiceProvider
                        .GetRequiredService<IUnitOfWorkManager>()
                        .Begin(new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true))
                    {
                        if (!await runs.TryLockLeaseForWriteAsync(run.BasicId, leaseToken, activeFencingVersion, cancellationToken))
                            throw new InvalidOperationException("The Run lease expired or was superseded before Candidate/Evidence/Score persistence.");
                        insertedCandidate = await candidates.AddAsync(candidate, cancellationToken);
                        var evidenceRows = CrmProspectingEvidenceFactory.Create(
                            insertedCandidate.BasicId,
                            envelope,
                            DateTimeOffset.UtcNow);
                        if (evidenceRows.Count > 0)
                        {
                            await evidenceRepository.AddRangeAsync(evidenceRows, cancellationToken);
                        }

                        // Persist a conservative, explainable score snapshot in the same transaction
                        // as Candidate and Evidence. Unknown procurement/product/market facts remain
                        // false; the scorecard therefore keeps unverified prospects on Hold.
                        var evaluatedAtUtc = DateTimeOffset.UtcNow;
                        var scoreInput = new CrmProspectingScoreInput(
                            HasVerifiableOrganization: false,
                            HasDirectProcurementSignal: false,
                            HasDirectProductFit: false,
                            HasTargetMarketFit: false,
                            PublishedAtUtc: record.PublishedAtUtc?.ToUniversalTime(),
                            HasTraceablePrimaryEvidence: evidenceRows.Any(evidence =>
                                (evidence.EvidenceType is "direct" or "registry") &&
                                evidence.VerificationStatus == "unverified" &&
                                !string.IsNullOrWhiteSpace(evidence.SourceUrlHash)),
                            HasPublicBusinessContact: !string.IsNullOrWhiteSpace(email),
                            IsKnownDuplicate: candidate.Status == CrmProspectingCandidateStatus.Duplicate,
                            IsRestrictedOrUnsafeSource: false);
                        var scoreResult = CrmProspectingScorecard.Evaluate(scoreInput, evaluatedAtUtc);
                        var scoreSnapshot = CrmProspectingScoreSnapshotFactory.Create(insertedCandidate.BasicId, scoreResult, evidenceRows, BuildInitialScoreIdempotencyKey(insertedCandidate.BasicId, scoreResult.ScorecardVersion));
                        await scope.ServiceProvider.GetRequiredService<ICrmProspectingScoreRepository>()
                            .AddAsync(scoreSnapshot, cancellationToken);

                        await unitOfWork.CompleteAsync(cancellationToken);
                    }
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
                if (candidateWasInserted && identityResolver is not null && gleifLookups < maxGleifLookupsPerRun)
                {
                    gleifLookups++;
                    try
                    {
                        var identity = await identityResolver.ResolveExactAsync(insertedCandidate.CompanyName, cancellationToken);
                        if (identity is not null)
                        {
                            var sourceUrl = identity.SourceUrl;
                            if (CrmProspectingEvidenceFactory.TryGetCanonicalSourceUrl(sourceUrl, out var canonicalSourceUrl))
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
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception identityException)
                    {
                        AddBoundedWarning(warnings, $"GLEIF identity lookup for candidate {insertedCandidate.BasicId} failed: {identityException.GetType().Name}.");
                        Logger.LogWarning(identityException, "GLEIF identity lookup failed for Candidate {CandidateId}", insertedCandidate.BasicId);
                    }
                }

                existingCandidateByKey[key] = insertedCandidate;

                existingKeys.Add(key);
                if (normalizedEmail is not null) seenCandidateEmails.Add(normalizedEmail);
                if (websiteHost is not null) seenCandidateWebsites.Add(websiteHost);
                candidateCount++;
            }

            var summary = warnings.Count == 0 ? null : string.Join(" | ", warnings).Trim();
            if (summary?.Length > 2000) summary = summary[..2000];

            var finalRunState = await runs.GetByIdAsync(run.BasicId, cancellationToken);
            var finalized = finalRunState?.Status == CrmProspectingRunStatus.CancelRequested
                ? await runs.CancelFencedWithDatabaseClockAsync(run.BasicId, leaseToken, activeFencingVersion, cancellationToken)
                : await runs.CompleteFencedWithDatabaseClockAsync(
                    run.BasicId, leaseToken, activeFencingVersion, serialized, raw.Count, failures,
                    summary, cancellationToken, candidateCount);
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
        return summary.Length > MaxFailureSummaryLength
            ? summary[..MaxFailureSummaryLength]
            : summary;
    }

    private const int MaxAggregatedWarnings = 100;
    private const int MaxAggregatedWarningLength = 500;

    private static void AddBoundedWarning(List<string> warnings, string? warning)
    {
        if (warnings.Count >= MaxAggregatedWarnings || string.IsNullOrWhiteSpace(warning)) return;
        // Provider output and exception messages are untrusted; strip control characters
        // before persisting them into the Run summary or exposing them in review screens.
        var value = string.Concat(warning.Select(character => char.IsControl(character) ? ' ' : character)).Trim();
        if (value.Length == 0) return;
        warnings.Add(value.Length > MaxAggregatedWarningLength
            ? value[..MaxAggregatedWarningLength]
            : value);
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

    private static string? NormalizeCandidateEmail(CrmProspectingRawRecord record)
    {
        var email = string.IsNullOrWhiteSpace(record.BusinessEmail) ? null : record.BusinessEmail.Trim();
        if (email?.Length > 254) email = null;
        return CrmLeadDuplicateMatcher.NormalizeEmail(email);
    }

    private static string? NormalizeCandidateWebsiteHost(CrmProspectingRawRecord record)
    {
        var website = string.IsNullOrWhiteSpace(record.Website) ? null : record.Website.Trim();
        if (website?.Length > 500) website = website[..500];
        return CrmLeadDuplicateMatcher.NormalizeDomain(website);
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
                // Recover lease-backed work first using PostgreSQL's clock. This is safe to run
                // at startup even while the legacy single-worker path remains active: only expired
                // rows carrying lease metadata are touched by this operation.
                await runs.RecoverExpiredLeasesAsync(cancellationToken);
                await runs.RecoverInterruptedAsync(DateTimeOffset.UtcNow, cancellationToken);
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
