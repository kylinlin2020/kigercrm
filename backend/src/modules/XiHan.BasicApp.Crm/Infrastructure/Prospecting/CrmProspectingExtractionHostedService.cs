using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Domain.DomainServices;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Tasks.BackgroundServices;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>Queue item for one tenant-scoped durable extraction job.</summary>
public sealed class CrmProspectingExtractionMessage : IBackgroundTaskItem
{
    public long JobId { get; init; }
    public long TenantId { get; init; }

    /// <summary>The exact claim token returned by PostgreSQL; a delayed queue item cannot adopt a later lease.</summary>
    public Guid LeaseToken { get; init; }

    /// <summary>The fencing generation associated with LeaseToken.</summary>
    public long FencingVersion { get; init; }

    public string TaskId => $"{JobId.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{FencingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    public object? Data => null;
}

/// <summary>
/// Consumes durable asynchronous provider jobs. Submission always reuses the persisted idempotency
/// key; polling and page retrieval are retriable reads; page writes and cursor advancement share
/// the candidate persistence transaction and the exact extraction lease fence.
/// </summary>
public sealed class CrmProspectingExtractionHostedService
    : XiHanBackgroundServiceBase<CrmProspectingExtractionHostedService>
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly string LeaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly IServiceScopeFactory _scopeFactory;

    public CrmProspectingExtractionHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<XiHanBackgroundServiceOptions> options,
        ILogger<CrmProspectingExtractionHostedService> logger)
        : base(logger, options, BuildConfig(options))
        => _scopeFactory = scopeFactory;

    protected override async Task<IEnumerable<IBackgroundTaskItem>> FetchWorkItemsAsync(
        int maxCount,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITenantDataScopeRunner>();
        var messages = new List<IBackgroundTaskItem>();
        await runner.RunAsync(async tenantId =>
        {
            if (tenantId is null || messages.Count >= maxCount) return;
            using var tenantScope = _scopeFactory.CreateScope();
            var repository = tenantScope.ServiceProvider.GetRequiredService<ICrmProspectingExtractionJobRepository>();
            var claimed = await repository.ClaimDueAsync(
                LeaseOwner, LeaseDuration, 1, cancellationToken);
            messages.AddRange(claimed.Select(job => new CrmProspectingExtractionMessage
            {
                JobId = job.BasicId,
                TenantId = tenantId.Value,
                LeaseToken = job.LeaseToken
                    ?? throw new InvalidOperationException("A claimed extraction job must include its lease token."),
                FencingVersion = job.FencingVersion
            }));
        }, cancellationToken);
        return messages;
    }

    protected override async Task ProcessItemAsync(IBackgroundTaskItem item, CancellationToken cancellationToken)
    {
        if (item is not CrmProspectingExtractionMessage message ||
            message.JobId <= 0 ||
            message.TenantId <= 0 ||
            message.LeaseToken == Guid.Empty ||
            message.FencingVersion <= 0)
        {
            Logger.LogWarning("Discarding malformed asynchronous extraction queue item");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(message.TenantId);
        var services = scope.ServiceProvider;
        var jobs = services.GetRequiredService<ICrmProspectingExtractionJobRepository>();
        var job = await jobs.GetByIdAsync(message.JobId, cancellationToken);

        // Queue messages are only wake-up signals. They must remain bound to the exact
        // lease generation acquired by FetchWorkItemsAsync; never adopt a newer token
        // from the database when a stale/duplicate queue message is delivered.
        if (job is null ||
            job.LeaseToken != message.LeaseToken ||
            job.FencingVersion != message.FencingVersion)
            return;

        var leaseToken = message.LeaseToken;

        // Queue latency can outlive the claim lease. Prove that this exact generation
        // is still live and extend it before any provider network operation begins.
        if (!await jobs.TryRenewLeaseAsync(
            job.BasicId, leaseToken, message.FencingVersion, LeaseDuration, cancellationToken))
            return;

        using var workCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var heartbeatStop = new CancellationTokenSource();
        var heartbeat = MaintainLeaseAsync(
            message.TenantId, job.BasicId, leaseToken, job.FencingVersion,
            workCancellation, heartbeatStop.Token);

        try
        {
            await ProcessClaimedJobAsync(services, jobs, job, leaseToken, workCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (workCancellation.IsCancellationRequested)
        {
            // The heartbeat cancels work when the exact lease can no longer be renewed.
            // Do not attempt any compensating writes with a stale fencing generation.
            Logger.LogWarning(
                "Asynchronous extraction job {JobId} stopped because its lease is no longer renewable",
                job.BasicId);
        }
        catch (Exception exception)
        {
            // Do not write exception messages to the database: provider exceptions may contain
            // request URLs, headers or credentials. The live lease expires and the stable
            // submission key/cursor makes the next claim safe to retry.
            Logger.LogWarning(exception,
                "Asynchronous extraction job {JobId} failed during {Status}; it will be recovered after its lease expires",
                job.BasicId, job.Status);
        }
        finally
        {
            heartbeatStop.Cancel();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested)
            {
                // Expected shutdown of the renewal loop after the work item finishes.
            }
        }
    }

    /// <summary>
    /// Renews a claimed job from an isolated tenant scope so heartbeat I/O never races
    /// the provider/page transaction using the work item's scoped repository.
    /// Losing or being unable to verify the lease cancels all in-flight provider work.
    /// </summary>
    private async Task MaintainLeaseAsync(
        long tenantId,
        long jobId,
        Guid leaseToken,
        long fencingVersion,
        CancellationTokenSource workCancellation,
        CancellationToken stopToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stopToken);
                using var scope = _scopeFactory.CreateScope();
                using var tenantScope = scope.ServiceProvider
                    .GetRequiredService<ICurrentTenant>().Change(tenantId);
                var jobs = scope.ServiceProvider.GetRequiredService<ICrmProspectingExtractionJobRepository>();
                var renewed = await jobs.TryRenewLeaseAsync(
                    jobId, leaseToken, fencingVersion, LeaseDuration, stopToken);
                if (renewed) continue;

                Logger.LogWarning(
                    "Lease renewal rejected for asynchronous extraction job {JobId}; cancelling in-flight work",
                    jobId);
                workCancellation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Normal item completion or host shutdown.
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception,
                "Lease renewal failed for asynchronous extraction job {JobId}; cancelling in-flight work",
                jobId);
            workCancellation.Cancel();
        }
    }

    private async Task ProcessClaimedJobAsync(
        IServiceProvider services,
        ICrmProspectingExtractionJobRepository jobs,
        SysCrmProspectingExtractionJob job,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        var registry = services.GetRequiredService<CrmProspectingProviderRegistry>();
        if (!registry.Asynchronous.TryGetValue(job.ProviderCode, out var provider) || !provider.SupportsIdempotentSubmission)
        {
            await MoveToNeedsReviewAsync(jobs, job, leaseToken, "async_provider_not_registered",
                "The asynchronous provider is not registered with a safe idempotent submission contract.", cancellationToken);
            return;
        }

        if (!Enum.TryParse<CrmProspectingExtractionStatus>(job.Status, ignoreCase: false, out var status) || !Enum.IsDefined(status))
            return;

        var runRepository = services.GetRequiredService<ICrmProspectingRunRepository>();
        var run = await runRepository.GetByIdAsync(job.RunId, cancellationToken);
        if (run is null)
        {
            await MoveToNeedsReviewAsync(jobs, job, leaseToken, "parent_run_missing",
                "The parent Run is unavailable in the current tenant.", cancellationToken);
            return;
        }

        if (run.Status == CrmProspectingRunStatus.CancelRequested)
        {
            if (status == CrmProspectingExtractionStatus.Submitting)
            {
                await MoveToNeedsReviewAsync(jobs, job, leaseToken, "cancel_during_ambiguous_submission",
                    "The parent Run was cancelled while remote submission acknowledgement was ambiguous.", cancellationToken);
                return;
            }
            if (status is CrmProspectingExtractionStatus.Pending or CrmProspectingExtractionStatus.Running)
                await provider.CancelExtractionAsync(
                    new CrmProspectingExtractionReference(job.ProviderCode, job.ProviderExtractionId!), cancellationToken);
            if (status is CrmProspectingExtractionStatus.Pending or CrmProspectingExtractionStatus.Running
                or CrmProspectingExtractionStatus.Succeeded or CrmProspectingExtractionStatus.FetchingResults)
                await jobs.TryTransitionAsync(job.BasicId, status,
                    CrmProspectingExtractionStatus.Cancelled, leaseToken, job.FencingVersion,
                    cancellationToken: cancellationToken);
            return;
        }

        var excludedDomains = ParseExcludedDomains(run.SnapshotExcludedDomainsJson);
        var request = CrmProspectingProviderRequestFactory.Create(run, excludedDomains);
        var fingerprint = CrmProspectingRequestFingerprint.Create(job.ProviderCode, request);
        if (!string.Equals(fingerprint, job.RequestFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            await MoveToNeedsReviewAsync(jobs, job, leaseToken, "request_fingerprint_mismatch",
                "The reconstructed immutable provider request does not match the persisted intent.", cancellationToken);
            return;
        }

        var decision = CrmProspectingExtractionWorkActionPolicy.Evaluate(status, job.ProviderExtractionId);
        switch (decision.Action)
        {
            case CrmProspectingExtractionWorkAction.Submit:
            {
                var submission = await provider.SubmitExtractionAsync(request, job.SubmissionIdempotencyKey, cancellationToken);
                if (!CrmProspectingExtractionSubmissionValidator.TryValidate(submission, job.ProviderCode, out var failure))
                {
                    await MoveToNeedsReviewAsync(jobs, job, leaseToken, failure,
                        "The provider returned an invalid asynchronous submission acknowledgement.", cancellationToken);
                    return;
                }

                var submittedAt = submission!.SubmittedAtUtc.ToUniversalTime();
                var nextPoll = CrmProspectingExtractionPollSchedule.GetNextPollAtUtc(
                    submittedAt, 0, providerPollAfter: submission.PollAfter);
                if (!await jobs.TryAcknowledgeSubmissionAsync(
                    job.BasicId, leaseToken, job.FencingVersion,
                    submission.Extraction.ProviderExtractionId,
                    submittedAt, nextPoll, submission.ExpiresAtUtc,
                    cancellationToken, submission.SafeMetadataJson))
                    throw new InvalidOperationException("The exact live lease could not persist the provider acknowledgement.");
                return;
            }

            case CrmProspectingExtractionWorkAction.Poll:
            {
                var extraction = new CrmProspectingExtractionReference(job.ProviderCode, job.ProviderExtractionId!);
                var poll = await provider.PollExtractionAsync(extraction, cancellationToken);
                var pollDecision = CrmProspectingExtractionPollPolicy.EvaluateForCurrentState(
                    status, poll, DateTimeOffset.UtcNow, job.PollAttemptCount);
                if (!await jobs.TryTransitionAsync(
                    job.BasicId, status, pollDecision.NextStatus, leaseToken, job.FencingVersion,
                    nextPollAtUtc: pollDecision.NextPollAtUtc,
                    lastPolledAtUtc: DateTimeOffset.UtcNow,
                    failureCode: pollDecision.FailureCode,
                    safeFailureSummary: pollDecision.SafeFailureSummary,
                    cancellationToken: cancellationToken))
                    throw new InvalidOperationException("The extraction poll transition lost its lease or lifecycle precondition.");
                return;
            }

            case CrmProspectingExtractionWorkAction.BeginResultFetch:
                if (!await jobs.TryTransitionAsync(
                    job.BasicId, CrmProspectingExtractionStatus.Succeeded,
                    CrmProspectingExtractionStatus.FetchingResults, leaseToken, job.FencingVersion,
                    nextPollAtUtc: DateTimeOffset.UtcNow, cancellationToken: cancellationToken))
                    throw new InvalidOperationException("The extraction could not enter FetchingResults under its live lease.");
                return;

            case CrmProspectingExtractionWorkAction.FetchResults:
            {
                var extraction = new CrmProspectingExtractionReference(job.ProviderCode, job.ProviderExtractionId!);
                var page = await provider.FetchExtractionPageAsync(extraction, job.ResultCursor, cancellationToken);
                if (!CrmProspectingExtractionPageValidator.TryValidate(page, out var failureCode) ||
                    !CrmProspectingExtractionPageValidator.CanCommitPage(page, job.ResultCursor))
                {
                    await MoveToFailedAsync(jobs, job, leaseToken, failureCode.Length == 0 ? "invalid_result_page" : failureCode,
                        cancellationToken);
                    return;
                }

                var items = await BuildCandidateItemsAsync(services, job, page, cancellationToken);
                var mappingFailures = page.Records.Count - page.FailureCount - items.Count;
                var persistablePage = mappingFailures == 0
                    ? page
                    : page with { FailureCount = checked(page.FailureCount + mappingFailures) };
                await services.GetRequiredService<CrmProspectingCandidatePersistenceService>()
                    .PersistPageAndAdvanceAsync(
                        job.BasicId, leaseToken, job.FencingVersion, job.ResultCursor,
                        persistablePage, items, cancellationToken);
                return;
            }

            default:
                if (decision.BlockReason is not null)
                    await MoveToNeedsReviewAsync(jobs, job, leaseToken, decision.BlockReason,
                        "The persisted extraction state does not permit an automatic provider operation.", cancellationToken);
                return;
        }
    }

    private static async Task<IReadOnlyList<CrmProspectingCandidatePageItem>> BuildCandidateItemsAsync(
        IServiceProvider services,
        SysCrmProspectingExtractionJob job,
        CrmProspectingExtractionPage page,
        CancellationToken cancellationToken)
    {
        var leadRepository = services.GetRequiredService<ICrmLeadRepository>();
        var candidateRepository = services.GetRequiredService<ICrmProspectingCandidateRepository>();
        var emails = page.Records.Select(CrmProspectingCandidateFactory.NormalizeEmail)
            .Where(value => value is not null).Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var websites = page.Records.Select(CrmProspectingCandidateFactory.NormalizeWebsiteHost)
            .Where(value => value is not null).Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var leadMatches = await leadRepository.GetDuplicateMatchesAsync(emails, websites, cancellationToken);
        var externalIds = page.Records.Select(CrmProspectingRecordIdentity.NormalizeExternalId).ToArray();
        var crossRun = await candidateRepository.GetCrossRunDuplicateMatchesAsync(
            job.RunId,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [job.ProviderCode] = externalIds
            },
            emails, websites, cancellationToken);

        var items = new List<CrmProspectingCandidatePageItem>(page.Records.Count);
        var seenExternalIds = new HashSet<string>(StringComparer.Ordinal);
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenWebsites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in page.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = CrmProspectingRecordIdentity.NormalizeExternalId(record);
            // The page persistence boundary requires one candidate per external identity.
            // Treat repeated provider identities as a bounded mapping failure rather than
            // allowing the whole page transaction to fail on a duplicate item.
            if (!seenExternalIds.Add(identity))
                continue;

            var companyName = string.IsNullOrWhiteSpace(record.CompanyName) ? record.Title : record.CompanyName;
            if (string.IsNullOrWhiteSpace(companyName))
                continue;

            var normalizedEmail = CrmProspectingCandidateFactory.NormalizeEmail(record);
            var websiteHost = CrmProspectingCandidateFactory.NormalizeWebsiteHost(record);
            var lead = leadMatches.FirstOrDefault(match =>
                (normalizedEmail is not null && string.Equals(match.DuplicateEmailNormalized, normalizedEmail, StringComparison.OrdinalIgnoreCase)) ||
                (websiteHost is not null && string.Equals(match.WebsiteHost, websiteHost, StringComparison.OrdinalIgnoreCase)));
            var priorCandidate = crossRun.FirstOrDefault(match =>
                (string.Equals(match.ProviderCode, job.ProviderCode, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(match.ExternalId, identity, StringComparison.Ordinal)) ||
                (normalizedEmail is not null && string.Equals(match.NormalizedEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase)) ||
                (websiteHost is not null && string.Equals(match.WebsiteHost, websiteHost, StringComparison.OrdinalIgnoreCase)));
            // Update both identity sets independently. Using a short-circuit OR here
            // would skip recording the website whenever the email was already a duplicate,
            // allowing a later row with that website to escape page-level duplicate detection.
            var duplicateEmailOnPage = normalizedEmail is not null && !seenEmails.Add(normalizedEmail);
            var duplicateWebsiteOnPage = websiteHost is not null && !seenWebsites.Add(websiteHost);
            var pageDuplicate = duplicateEmailOnPage || duplicateWebsiteOnPage;
            var duplicateReason = lead is not null ? "Email or website matches an existing CRM lead"
                : priorCandidate is not null ? "Matches a candidate from an earlier Run"
                : pageDuplicate ? "Duplicate contact details within this extraction page" : null;
            var candidate = CrmProspectingCandidateFactory.Create(
                job.RunId, job.ProviderCode, record, identity, companyName.Trim(),
                normalizedEmail, websiteHost, duplicateReason is not null, lead?.BasicId, duplicateReason);
            items.Add(new CrmProspectingCandidatePageItem(
                candidate, new CrmProspectingRawEnvelope(job.ProviderCode, record)));
        }
        return items;
    }

    private static string[] ParseExcludedDomains(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        var values = JsonSerializer.Deserialize<string[]>(json)
            ?? throw new InvalidOperationException("The immutable excluded-domain snapshot is invalid.");
        if (values.Length > 500 || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 253))
            throw new InvalidOperationException("The immutable excluded-domain snapshot exceeds its bounds.");
        return values;
    }

    private static Task<bool> MoveToNeedsReviewAsync(
        ICrmProspectingExtractionJobRepository jobs,
        SysCrmProspectingExtractionJob job,
        Guid leaseToken,
        string failureCode,
        string summary,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CrmProspectingExtractionStatus>(job.Status, out var current) || !Enum.IsDefined(current))
            return Task.FromResult(false);
        var next = CrmProspectingExtractionStateMachine.CanTransition(current, CrmProspectingExtractionStatus.NeedsReview)
            ? CrmProspectingExtractionStatus.NeedsReview
            : CrmProspectingExtractionStatus.Failed;
        return jobs.TryTransitionAsync(job.BasicId, current, next, leaseToken, job.FencingVersion,
            failureCode: failureCode, safeFailureSummary: summary, cancellationToken: cancellationToken);
    }

    private static Task MoveToFailedAsync(
        ICrmProspectingExtractionJobRepository jobs,
        SysCrmProspectingExtractionJob job,
        Guid leaseToken,
        string failureCode,
        CancellationToken cancellationToken)
        => jobs.TryTransitionAsync(job.BasicId, CrmProspectingExtractionStatus.FetchingResults,
            CrmProspectingExtractionStatus.Failed, leaseToken, job.FencingVersion,
            failureCode: failureCode, safeFailureSummary: "The provider returned an invalid result page.",
            cancellationToken: cancellationToken);

    private static IDynamicServiceConfig BuildConfig(IOptions<XiHanBackgroundServiceOptions> options)
    {
        var config = new DynamicServiceConfig(options);
        config.UpdateMaxConcurrentTasks(1);
        config.UpdateIdleDelay(5_000);
        return config;
    }
}
