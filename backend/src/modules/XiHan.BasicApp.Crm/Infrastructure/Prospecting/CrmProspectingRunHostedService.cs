using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Domain.Services;
using XiHan.BasicApp.Saas.Domain.DomainServices;
using XiHan.Framework.Caching.Distributed.Abstracts;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Tasks.BackgroundServices;

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
/// Executes approved prospecting provider plans. Recovery follows the existing SaaS export worker pattern
/// and therefore requires a single active application worker until lease/fencing support is implemented.
/// </summary>
public sealed class CrmProspectingRunHostedService : XiHanBackgroundServiceBase<CrmProspectingRunHostedService>
{
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
        try
        {
            using var scope = _scopeFactory.CreateScope();
            using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(message.TenantId);

            var runs = scope.ServiceProvider.GetRequiredService<ICrmProspectingRunRepository>();
            var run = await runs.ClaimQueuedAsync(message.RunId, DateTimeOffset.UtcNow, cancellationToken);
            if (run is null) return;

            var providers = scope.ServiceProvider.GetServices<ICrmProspectingProvider>()
                .GroupBy(provider => provider.ProviderCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var plan = ParseStringArray(run.SnapshotProviderPlanJson, "provider plan");
            if (plan.Count == 0) throw new InvalidOperationException("The provider plan contains no usable provider codes.");
            var excludedDomains = ParseStringArray(run.SnapshotExcludedDomainsJson, "excluded domains", allowNull: true);
            var raw = new List<CrmProspectingRawEnvelope>();
            var failures = 0;
            var warnings = new List<string>();

            foreach (var providerCode in plan.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var current = await runs.GetByIdAsync(run.BasicId, cancellationToken);
                if (current?.Status == CrmProspectingRunStatus.CancelRequested)
                    break;

                if (!providers.TryGetValue(providerCode, out var provider))
                {
                    failures++;
                    warnings.Add($"Provider is not registered: {providerCode}");
                    continue;
                }

                try
                {
                    var request = new CrmProspectingProviderRequest(
                        run.BasicId,
                        run.SnapshotSearchObjective,
                        run.SnapshotProductScope ?? string.Empty,
                        run.SnapshotTargetMarkets ?? string.Empty,
                        run.SnapshotIdealCustomerProfile ?? string.Empty,
                        "{}",
                        excludedDomains);
                    var result = await provider.SearchAsync(request, cancellationToken);
                    failures += Math.Max(0, result.FailureCount);
                    warnings.AddRange(result.Warnings.Where(value => !string.IsNullOrWhiteSpace(value)));
                    const int maxRawRecordsPerRun = 2000;
                    var remaining = Math.Max(0, maxRawRecordsPerRun - raw.Count);
                    foreach (var record in result.Records.Take(remaining))
                        raw.Add(new CrmProspectingRawEnvelope(provider.ProviderCode, record));
                    if (result.Records.Count > remaining)
                    {
                        failures++;
                        warnings.Add($"Raw result cap reached; {result.Records.Count - remaining} provider records were not retained.");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures++;
                    warnings.Add($"{providerCode}: {ex.GetType().Name}: {ex.Message}");
                    Logger.LogWarning(ex, "Prospecting provider failed for Run {RunId}, provider {Provider}", run.BasicId, providerCode);
                }
            }


            var serialized = JsonSerializer.Serialize(raw);
            if (serialized.Length > 5_000_000)
                throw new InvalidOperationException("Serialized provider output exceeds the 5 MB Run storage limit.");

            var beforeCandidates = await runs.GetByIdAsync(run.BasicId, cancellationToken);
            var candidates = scope.ServiceProvider.GetRequiredService<ICrmProspectingCandidateRepository>();
            var existingCandidates = await candidates.GetListAsync(candidate => candidate.RunId == run.BasicId, cancellationToken);
            if (beforeCandidates?.Status == CrmProspectingRunStatus.CancelRequested)
            {
                await runs.CompleteAsync(
                    run.BasicId, serialized, raw.Count, failures,
                    "Run cancelled before candidate normalization.", DateTimeOffset.UtcNow,
                    cancellationToken, existingCandidates.Count);
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
            var existingKeys = existingCandidates
                .Select(candidate => $"{candidate.ProviderCode}::{candidate.ExternalId}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidateCount = existingCandidates.Count;
            var seenCandidateEmails = existingCandidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.NormalizedEmail))
                .Select(candidate => candidate.NormalizedEmail!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seenCandidateWebsites = existingCandidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.WebsiteHost))
                .Select(candidate => candidate.WebsiteHost!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var envelope in raw)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var runState = await runs.GetByIdAsync(run.BasicId, cancellationToken);
                if (runState?.Status == CrmProspectingRunStatus.CancelRequested) break;
                var record = envelope.Record;
                var providerCode = envelope.ProviderCode.Trim();
                if (string.IsNullOrWhiteSpace(providerCode)) throw new InvalidOperationException("Provider code cannot be empty.");
                var externalId = string.IsNullOrWhiteSpace(record.ExternalId)
                    ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record))))
                    : record.ExternalId.Trim();
                if (providerCode.Length > 80) throw new InvalidOperationException("Provider code exceeds 80 characters.");
                if (externalId.Length > 200) externalId = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(externalId)));
                var key = $"{providerCode}::{externalId}";
                if (existingKeys.Contains(key)) continue;

                var companyName = (string.IsNullOrWhiteSpace(record.CompanyName) ? record.Title : record.CompanyName)?.Trim();
                if (string.IsNullOrWhiteSpace(companyName))
                {
                    failures++;
                    warnings.Add($"Provider {providerCode} returned a record without company name/title.");
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
                await candidates.AddAsync(candidate, cancellationToken);
                existingKeys.Add(key);
                if (normalizedEmail is not null) seenCandidateEmails.Add(normalizedEmail);
                if (websiteHost is not null) seenCandidateWebsites.Add(websiteHost);
                candidateCount++;
            }

            var summary = warnings.Count == 0 ? null : string.Join(" | ", warnings).Trim();
            if (summary?.Length > 2000) summary = summary[..2000];

            await runs.CompleteAsync(
                run.BasicId,
                serialized,
                raw.Count,
                failures,
                summary,
                DateTimeOffset.UtcNow,
                cancellationToken,
                candidateCount);
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
                await scope.ServiceProvider.GetRequiredService<ICrmProspectingRunRepository>()
                    .FailAsync(message.RunId, $"{ex.GetType().Name}: {ex.Message}", DateTimeOffset.UtcNow, cancellationToken);
            }
            catch (Exception persistException)
            {
                Logger.LogError(persistException, "Could not persist prospecting Run failure: {RunId}", message.RunId);
            }
        }
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
                await runs.RecoverInterruptedAsync(DateTimeOffset.UtcNow, cancellationToken);
                var ids = await runs.GetQueuedIdsAsync(cancellationToken);
                foreach (var id in ids)
                {
                    await _queue.EnqueueAsync(
                        new CrmProspectingRunMessage
                        {
                            RunId = id,
                            TenantId = tenantId ?? 0,
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
