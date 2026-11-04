using System.Text.Json;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;

/// <summary>Enforces immutable strategy versions and explicit approval before activation.</summary>
public sealed class CrmProspectingDomainService(
    ICrmProspectingCampaignRepository campaigns,
    ICrmProspectingStrategyRepository strategies,
    ICrmProspectingRunRepository runs) : ICrmProspectingDomainService
{
    public async Task<SysCrmProspectingCampaign> CreateCampaignAsync(SysCrmProspectingCampaign campaign, CancellationToken cancellationToken = default)
    {
        ValidateCampaign(campaign);
        campaign.Status = CrmProspectingCampaignStatus.Draft;
        return await campaigns.AddAsync(campaign, cancellationToken);
    }

    public async Task<SysCrmProspectingCampaign> UpdateCampaignAsync(SysCrmProspectingCampaign campaign, CancellationToken cancellationToken = default)
    {
        if (campaign.BasicId <= 0) throw new ArgumentOutOfRangeException(nameof(campaign.BasicId));
        ValidateCampaign(campaign);
        var existing = await campaigns.GetByIdAsync(campaign.BasicId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        if (existing.Status is CrmProspectingCampaignStatus.Completed or CrmProspectingCampaignStatus.Archived)
            throw new InvalidOperationException("Completed or archived campaigns cannot be edited.");
        existing.Name = campaign.Name;
        existing.Objective = campaign.Objective;
        existing.ProductScope = campaign.ProductScope;
        existing.TargetMarkets = campaign.TargetMarkets;
        existing.IdealCustomerProfile = campaign.IdealCustomerProfile;
        existing.OwnerUserId = campaign.OwnerUserId;
        return await campaigns.UpdateAsync(existing, cancellationToken);
    }

    public async Task<SysCrmProspectingStrategy> CreateStrategyVersionAsync(SysCrmProspectingStrategy strategy, CancellationToken cancellationToken = default)
    {
        ValidateStrategy(strategy);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var campaign = await campaigns.GetByIdAsync(strategy.CampaignId, cancellationToken)
                ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
            if (campaign.Status is CrmProspectingCampaignStatus.Completed or CrmProspectingCampaignStatus.Archived)
                throw new InvalidOperationException("A completed or archived campaign cannot receive a new strategy.");

            var expectedSequence = campaign.StrategyVersionSequence;
            var nextSequence = checked(expectedSequence + 1);
            if (!await campaigns.TryAdvanceStrategyVersionAsync(campaign.BasicId, expectedSequence, nextSequence, cancellationToken))
                continue;

            strategy.Version = nextSequence;
            strategy.ApprovalStatus = CrmProspectingStrategyApprovalStatus.Draft;
            strategy.ApprovedBy = null;
            strategy.ApprovedAt = null;
            strategy.ActivatedAt = null;
            return await strategies.AddAsync(strategy, cancellationToken);
        }

        throw new InvalidOperationException("Strategy version allocation is busy. Refresh the campaign and retry.");
    }

    public async Task<SysCrmProspectingStrategy> SubmitStrategyForApprovalAsync(long strategyId, CancellationToken cancellationToken = default)
    {
        var strategy = await GetStrategyAsync(strategyId, cancellationToken);
        if (strategy.ApprovalStatus != CrmProspectingStrategyApprovalStatus.Draft)
            throw new InvalidOperationException("Only a draft strategy can be submitted for approval.");
        if (!await strategies.TryTransitionApprovalAsync(strategy.BasicId,
                CrmProspectingStrategyApprovalStatus.Draft,
                CrmProspectingStrategyApprovalStatus.PendingApproval,
                null, DateTimeOffset.UtcNow, cancellationToken))
            throw new InvalidOperationException("The strategy approval state changed. Refresh before submitting again.");
        strategy.ApprovalStatus = CrmProspectingStrategyApprovalStatus.PendingApproval;
        return strategy;
    }

    public async Task<SysCrmProspectingStrategy> DecideStrategyAsync(long strategyId, bool approve, long? approverId, CancellationToken cancellationToken = default)
    {
        var strategy = await GetStrategyAsync(strategyId, cancellationToken);
        if (strategy.ApprovalStatus != CrmProspectingStrategyApprovalStatus.PendingApproval)
            throw new InvalidOperationException("Only a pending strategy can receive an approval decision.");
        if (approverId is null or <= 0)
            throw new ArgumentOutOfRangeException(nameof(approverId), "An authenticated approver ID is required.");
        var nextStatus = approve ? CrmProspectingStrategyApprovalStatus.Approved : CrmProspectingStrategyApprovalStatus.Rejected;
        var decidedAt = DateTimeOffset.UtcNow;
        if (!await strategies.TryTransitionApprovalAsync(strategy.BasicId,
                CrmProspectingStrategyApprovalStatus.PendingApproval,
                nextStatus, approverId, decidedAt, cancellationToken))
            throw new InvalidOperationException("The strategy approval state changed. Refresh before deciding again.");
        strategy.ApprovalStatus = nextStatus;
        strategy.ApprovedBy = approverId;
        strategy.ApprovedAt = decidedAt;
        return strategy;
    }

    public async Task<SysCrmProspectingCampaign> ActivateStrategyAsync(long strategyId, CancellationToken cancellationToken = default)
    {
        var strategy = await GetStrategyAsync(strategyId, cancellationToken);
        if (strategy.ApprovalStatus != CrmProspectingStrategyApprovalStatus.Approved)
            throw new InvalidOperationException("Only an approved strategy can be activated.");
        var campaign = await campaigns.GetByIdAsync(strategy.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        if (campaign.Status is CrmProspectingCampaignStatus.Completed or CrmProspectingCampaignStatus.Archived)
            throw new InvalidOperationException("A completed or archived campaign cannot activate a strategy.");
        // Repeating activation of the already-active strategy is an idempotent no-op.
        if (campaign.ActiveStrategyVersion == strategy.Version && strategy.ActivatedAt != null)
            return campaign;

        var expectedVersion = campaign.ActiveStrategyVersion;
        if (!await campaigns.TrySetActiveStrategyVersionAsync(campaign.BasicId, expectedVersion, strategy.Version, cancellationToken))
            throw new InvalidOperationException("The campaign active strategy changed concurrently. Refresh before activating again.");

        // The campaign pointer compare-and-swap is the serialization gate. All following writes
        // participate in this application's transaction, so a failure rolls the pointer back too.
        await strategies.ClearOtherActiveVersionsAsync(campaign.BasicId, strategy.BasicId, cancellationToken);
        if (!await strategies.TryMarkActivatedAsync(campaign.BasicId, strategy.BasicId, DateTimeOffset.UtcNow, cancellationToken))
            throw new InvalidOperationException("The approved strategy changed before activation could complete.");

        return await campaigns.GetByIdAsync(campaign.BasicId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign disappeared during strategy activation.");
    }

    public async Task<SysCrmProspectingRun> CreateRunAsync(long campaignId, string requestKey, CancellationToken cancellationToken = default)
    {
        if (campaignId <= 0) throw new ArgumentOutOfRangeException(nameof(campaignId));
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        requestKey = requestKey.Trim();
        if (requestKey.Length > 100) throw new ArgumentOutOfRangeException(nameof(requestKey));

        var campaign = await campaigns.GetByIdAsync(campaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        if (campaign.Status != CrmProspectingCampaignStatus.Active || campaign.ActiveStrategyVersion is null)
            throw new InvalidOperationException("Only an active campaign with an active strategy can be executed.");

        var activeStrategies = await strategies.GetListAsync(
            x => x.CampaignId == campaignId && x.Version == campaign.ActiveStrategyVersion.Value && x.ActivatedAt != null,
            cancellationToken);
        var strategy = activeStrategies.SingleOrDefault()
            ?? throw new InvalidOperationException("The campaign active strategy version is missing or ambiguous.");
        if (strategy.ApprovalStatus != CrmProspectingStrategyApprovalStatus.Approved)
            throw new InvalidOperationException("The active strategy is not approved.");
        ValidateProviderPlan(strategy.ProviderPlanJson);
        var snapshot = new
        {
            CampaignId = campaign.BasicId,
            StrategyId = strategy.BasicId,
            StrategyVersion = strategy.Version,
            SearchObjective = strategy.SearchObjective,
            ProductScope = campaign.ProductScope,
            TargetMarkets = campaign.TargetMarkets,
            IdealCustomerProfile = campaign.IdealCustomerProfile,
            ProviderPlanJson = strategy.ProviderPlanJson,
            ExcludedDomainsJson = strategy.ExcludedDomainsJson
        };
        var snapshotJson = JsonSerializer.Serialize(snapshot);
        var payloadHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(snapshotJson)));
        var existing = (await runs.GetListAsync(x => x.RequestKey == requestKey, cancellationToken)).SingleOrDefault();
        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                throw new InvalidOperationException("The request key was already used with a different prospecting execution payload.");
            return existing;
        }

        var run = new SysCrmProspectingRun
        {
            CampaignId = campaign.BasicId,
            StrategyId = strategy.BasicId,
            StrategyVersion = strategy.Version,
            RequestKey = requestKey,
            PayloadHash = payloadHash,
            SnapshotSearchObjective = strategy.SearchObjective,
            SnapshotProductScope = campaign.ProductScope,
            SnapshotTargetMarkets = campaign.TargetMarkets,
            SnapshotIdealCustomerProfile = campaign.IdealCustomerProfile,
            SnapshotProviderPlanJson = strategy.ProviderPlanJson,
            SnapshotExcludedDomainsJson = strategy.ExcludedDomainsJson,
            Status = CrmProspectingRunStatus.Queued
        };
        return await runs.AddAsync(run, cancellationToken);
    }

    public async Task<SysCrmProspectingRun?> GetRunAsync(long runId, CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        return await runs.GetByIdAsync(runId, cancellationToken);
    }

    public async Task<SysCrmProspectingRun> CancelRunAsync(long runId, CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        var run = await runs.GetByIdAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        if (run.Status == CrmProspectingRunStatus.Queued)
        {
            run.Status = CrmProspectingRunStatus.Cancelled;
            run.CompletedAt = DateTimeOffset.UtcNow;
        }
        else if (run.Status == CrmProspectingRunStatus.Running)
        {
            run.Status = CrmProspectingRunStatus.CancelRequested;
        }
        else if (run.Status == CrmProspectingRunStatus.CancelRequested || run.Status == CrmProspectingRunStatus.Cancelled)
        {
            return run;
        }
        else
        {
            throw new InvalidOperationException("A completed prospecting run cannot be cancelled.");
        }
        return await runs.UpdateAsync(run, cancellationToken);
    }

    private async Task<SysCrmProspectingStrategy> GetStrategyAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        return await strategies.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting strategy does not exist.");
    }

    private static void ValidateCampaign(SysCrmProspectingCampaign campaign)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaign.Name);
        campaign.Name = campaign.Name.Trim();
        if (campaign.Name.Length > 160) throw new ArgumentOutOfRangeException(nameof(campaign.Name));
        if (campaign.Objective?.Length > 1000) throw new ArgumentOutOfRangeException(nameof(campaign.Objective));
        if (campaign.ProductScope?.Length > 500) throw new ArgumentOutOfRangeException(nameof(campaign.ProductScope));
        if (campaign.OwnerUserId is <= 0) throw new ArgumentOutOfRangeException(nameof(campaign.OwnerUserId));
    }

    private static void ValidateStrategy(SysCrmProspectingStrategy strategy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategy.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategy.SearchObjective);
        if (strategy.Name.Trim().Length > 160) throw new ArgumentOutOfRangeException(nameof(strategy.Name));
        ValidateProviderPlan(strategy.ProviderPlanJson);
        if (strategy.ExcludedDomainsJson is not null)
            ValidateJsonArray(strategy.ExcludedDomainsJson, nameof(strategy.ExcludedDomainsJson));
        strategy.Name = strategy.Name.Trim();
        strategy.SearchObjective = strategy.SearchObjective.Trim();
    }

    private const int MaxProviderPlanEntries = 32;
    private const int MaxProviderCodeLength = 80;

    private static void ValidateProviderPlan(string json)
    {
        ValidateJsonArray(json, nameof(SysCrmProspectingStrategy.ProviderPlanJson));
        using var document = JsonDocument.Parse(json);
        var entries = document.RootElement.EnumerateArray().ToArray();
        if (entries.Length == 0)
            throw new ArgumentException("Provider plan cannot be empty.", nameof(json));
        if (entries.Length > MaxProviderPlanEntries)
            throw new ArgumentOutOfRangeException(nameof(json), $"Provider plan cannot contain more than {MaxProviderPlanEntries} entries.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Every provider plan entry must be a string.", nameof(json));

            var providerCode = entry.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(providerCode))
                throw new ArgumentException("Provider plan entries cannot be blank.", nameof(json));
            if (providerCode.Length > MaxProviderCodeLength)
                throw new ArgumentOutOfRangeException(nameof(json), $"Provider codes cannot exceed {MaxProviderCodeLength} characters.");
            if (providerCode.Any(char.IsControl))
                throw new ArgumentException("Provider codes cannot contain control characters.", nameof(json));
            if (!seen.Add(providerCode))
                throw new ArgumentException("Provider plan entries must be unique (case-insensitive).", nameof(json));
        }
    }

    private static void ValidateJsonArray(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Expected a JSON array.", name);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Invalid JSON array.", name, exception);
        }
    }
}
