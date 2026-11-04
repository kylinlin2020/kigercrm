using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level guards for prospecting application workflow boundaries.</summary>
public sealed class CrmProspectingApplicationWiringTests
{
    [Fact]
    public void ProspectingApplication_ShouldExposeCampaignAndStrategyLifecycle()
    {
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Contracts", "ICrmProspectingAppService.cs");
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmProspectingAppService.cs");
        foreach (var operation in new[] { "CreateCampaignAsync", "UpdateCampaignAsync", "GetCampaignsAsync", "GetCampaignAsync", "CreateStrategyVersionAsync", "SubmitStrategyForApprovalAsync", "DecideStrategyAsync", "ActivateStrategyAsync", "GetStrategiesAsync", "GetCampaignRunsAsync", "StartRunAsync", "GetRunAsync", "CancelRunAsync" })
        {
            Assert.Contains(operation, contract, StringComparison.Ordinal);
            Assert.Contains(operation, service, StringComparison.Ordinal);
        }
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Update)]", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", service, StringComparison.Ordinal);
        Assert.Contains("GetLatestByCampaignAsync(campaignId, 500", service, StringComparison.Ordinal);
        Assert.Contains("GetLatestAccessibleAsync(scope.Unrestricted, scope.UserIds, 500", service, StringComparison.Ordinal);
        Assert.Contains("ResolveAccessibleUsersAsync", service, StringComparison.Ordinal);
        Assert.Contains("entity.OwnerUserId ??= CurrentUser?.Id", service, StringComparison.Ordinal);
        Assert.Contains("GetLatestByCampaignAsync(campaignId, 500", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingRun_ShouldRequireApprovedActiveStrategyAndPersistRequestFingerprint()
    {
        var domain = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "DomainServices", "Implementations", "CrmProspectingDomainService.cs");
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingRun.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.12", "5.9.12.sql");
        Assert.Contains("Only an active campaign with an active strategy can be executed.", domain, StringComparison.Ordinal);
        Assert.Contains("An empty provider plan cannot be executed.", domain, StringComparison.Ordinal);
        Assert.Contains("PayloadHash = payloadHash", domain, StringComparison.Ordinal);
        Assert.Contains("SnapshotSearchObjective = strategy.SearchObjective", domain, StringComparison.Ordinal);
        Assert.Contains("SnapshotProductScope = campaign.ProductScope", domain, StringComparison.Ordinal);
        Assert.Contains("JsonSerializer.Serialize(snapshot)", domain, StringComparison.Ordinal);
        Assert.Contains("RequestKey", entity, StringComparison.Ordinal);
        Assert.Contains("Payload_Hash", entity, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS payload_hash", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingDomainService_ShouldBeExplicitlyRegistered()
    {
        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        Assert.Contains("AddScoped<ICrmProspectingDomainService, CrmProspectingDomainService>()", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingDomain_ShouldRequireApprovalBeforeActivationAndValidateProviderJson()
    {
        var domain = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "DomainServices", "Implementations", "CrmProspectingDomainService.cs");
        Assert.Contains("Only an approved strategy can be activated.", domain, StringComparison.Ordinal);
        Assert.Contains("Only a pending strategy can receive an approval decision.", domain, StringComparison.Ordinal);
        Assert.Contains("JsonDocument.Parse", domain, StringComparison.Ordinal);
        Assert.Contains("GetListAsync", domain, StringComparison.Ordinal);
        Assert.Contains("Only a draft strategy can be submitted for approval.", domain, StringComparison.Ordinal);
        Assert.Contains("strategy.Version", domain, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingStrategyApprovalStatus.Approved", domain, StringComparison.Ordinal);
    }


    [Fact]
    public void ProspectingProviderBoundary_ShouldRegisterOnlyAnExplicitSyntheticSmokeAdapter()
    {
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Prospecting", "CrmProspectingProviderContracts.cs");
        var fake = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "FakeCrmProspectingProvider.cs");
        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        Assert.Contains("interface ICrmProspectingProvider", contract, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRawRecord", contract, StringComparison.Ordinal);
        Assert.Contains("must not create or update CRM leads directly", contract, StringComparison.Ordinal);
        Assert.Contains("ProviderCode => \"fake\"", fake, StringComparison.Ordinal);
        Assert.Contains("example.com", fake, StringComparison.Ordinal);
        Assert.Contains("performs no network requests", fake, StringComparison.Ordinal);
        Assert.Contains("XiHan:CRM:Prospecting:EnableFakeProvider", registration, StringComparison.Ordinal);
        Assert.Contains("bool.TryParse(configuration[", registration, StringComparison.Ordinal);
        Assert.Contains("&& enableFakeProvider", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSingleton<ICrmProspectingProvider, FakeCrmProspectingProvider>();", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingRunRepository_ShouldUseConditionalClaimAndRecoverInterruptedStates()
    {
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Repositories", "ICrmProspectingRunRepository.cs");
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        Assert.Contains("ClaimQueuedAsync", contract, StringComparison.Ordinal);
        Assert.Contains("GetQueuedIdsAsync", contract, StringComparison.Ordinal);
        Assert.Contains("RecoverInterruptedAsync", contract, StringComparison.Ordinal);
        Assert.Contains("run.Status == CrmProspectingRunStatus.Queued", repository, StringComparison.Ordinal);
        Assert.Contains("Status = CrmProspectingRunStatus.Running", repository, StringComparison.Ordinal);
        Assert.Contains("Status = CrmProspectingRunStatus.Queued", repository, StringComparison.Ordinal);
        Assert.Contains("run.Status == CrmProspectingRunStatus.CancelRequested", repository, StringComparison.Ordinal);
        Assert.Contains("Status = CrmProspectingRunStatus.Cancelled", repository, StringComparison.Ordinal);
        Assert.Contains("CreateQueryable()", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingRunWorker_ShouldPersistRawProviderOutputAndTerminalStatus()
    {
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingRun.cs");
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmProspectingAppService.cs");
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");
        var candidate = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingCandidate.cs");
        var candidateMigration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.14", "5.9.14.sql");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.13", "5.9.13.sql");
        Assert.Contains("Raw_Results_Json", entity, StringComparison.Ordinal);
        Assert.Contains("BuildFailureSummary(ex)", worker, StringComparison.Ordinal);
        Assert.Contains("MaxFailureSummaryLength = 2000", worker, StringComparison.Ordinal);
        Assert.Contains("MaxFailureSummaryLength = 2000", repository, StringComparison.Ordinal);
        Assert.Contains("failureSummary = BoundFailureSummary(failureSummary)", repository, StringComparison.Ordinal);
        Assert.Contains("failureSummary = BoundFailureSummary(failureSummary) ?? string.Empty", repository, StringComparison.Ordinal);
        Assert.Contains("RawResultsJson = rawResultsJson", repository, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunStatus.Succeeded", repository, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunStatus.PartiallySucceeded", repository, StringComparison.Ordinal);
        Assert.Contains("OnCompleted", app, StringComparison.Ordinal);
        Assert.Contains("ICrmProspectingProvider", worker, StringComparison.Ordinal);
        Assert.Contains("ITenantDataScopeRunner", worker, StringComparison.Ordinal);
        Assert.Contains("SnapshotProviderPlanJson", worker, StringComparison.Ordinal);
        Assert.Contains("SnapshotSearchObjective", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("campaign.ProductScope ?? string.Empty", worker, StringComparison.Ordinal);
        Assert.Contains("CandidateCount = candidateCount", repository, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS raw_results_json text", migration, StringComparison.Ordinal);
        Assert.Contains("Raw result cap reached", worker, StringComparison.Ordinal);
        Assert.Contains("5_000_000", worker, StringComparison.Ordinal);
        Assert.Contains("Encoding.UTF8.GetByteCount(serialized) > 5_000_000", worker, StringComparison.Ordinal);
        Assert.Contains("PendingReview", candidate, StringComparison.Ordinal);
        Assert.Contains("DuplicateLeadId", candidate, StringComparison.Ordinal);
        Assert.Contains("CrmLeadDuplicateMatcher.NormalizeEmail", worker, StringComparison.Ordinal);
        Assert.Contains("lead.WebsiteHost == websiteHost", worker, StringComparison.Ordinal);
        Assert.Contains("GetDuplicateMatchesAsync", worker, StringComparison.Ordinal);
        Assert.Contains("var afterProvider = await runs.GetByIdAsync(run.BasicId, cancellationToken)", worker, StringComparison.Ordinal);
        Assert.Contains("afterProvider?.Status == CrmProspectingRunStatus.CancelRequested", worker, StringComparison.Ordinal);
        Assert.Contains("AddBoundedProviderWarnings(warnings, result.Warnings)", worker, StringComparison.Ordinal);
        Assert.Contains("const int MaxAggregatedWarnings = 100", worker, StringComparison.Ordinal);
        Assert.Contains("const int MaxAggregatedWarningLength = 500", worker, StringComparison.Ordinal);
        Assert.Contains("AddBoundedWarning(warnings,", worker, StringComparison.Ordinal);
        Assert.Equal(1, worker.Split("warnings.Add(", StringSplitOptions.None).Length - 1);
        var providerCallPosition = worker.IndexOf("result = await provider.SearchAsync", StringComparison.Ordinal);
        var providerCatchPosition = worker.IndexOf("catch (Exception ex)", providerCallPosition, StringComparison.Ordinal);
        var providerContinuePosition = worker.IndexOf("continue;", providerCatchPosition, StringComparison.Ordinal);
        var postProviderCheckPosition = worker.IndexOf("var afterProvider = await runs.GetByIdAsync", StringComparison.Ordinal);
        Assert.True(providerCallPosition >= 0 && providerCatchPosition > providerCallPosition, "Provider invocation must be isolated by its own exception handler.");
        Assert.True(providerContinuePosition > providerCatchPosition && postProviderCheckPosition > providerContinuePosition, "Post-provider state reads must be outside the provider exception handler.");
        Assert.Contains("const int cancellationPollInterval = 25", worker, StringComparison.Ordinal);
        Assert.Contains("recordIndex % cancellationPollInterval == 0", worker, StringComparison.Ordinal);
        Assert.Contains("for (var recordIndex = 0; recordIndex < raw.Count; recordIndex++)", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var envelope in raw)", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("leadRepository.GetListAsync", worker, StringComparison.Ordinal);
        var leadRepository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmLeadRepository.cs");
        Assert.Contains("const int batchSize = 300", leadRepository, StringComparison.Ordinal);
        Assert.Contains("keys.Contains(lead.DuplicateEmailNormalized)", leadRepository, StringComparison.Ordinal);
        Assert.Contains("keys.Contains(lead.WebsiteHost)", leadRepository, StringComparison.Ordinal);
        Assert.Contains("CreateQueryable()", leadRepository, StringComparison.Ordinal);
        Assert.Contains("crm_prospecting_candidate", candidateMigration, StringComparison.Ordinal);
        Assert.Contains("CandidateCount = candidateCount", Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs"), StringComparison.Ordinal);
        Assert.Contains("GetRunCandidatesAsync", app, StringComparison.Ordinal);
        Assert.Contains("GetLatestByRunAsync(runId, candidateStatus, 500", app, StringComparison.Ordinal);
        Assert.Contains("ReviewCandidateAsync", app, StringComparison.Ordinal);
        Assert.Contains("Duplicate candidate approval requires an explicit duplicate override", app, StringComparison.Ordinal);
        Assert.Contains("ConvertApprovedCandidateToLeadAsync", app, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingCandidateStatus.Approved", app, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingCandidateStatus.Converting", app, StringComparison.Ordinal);
        Assert.Contains("The conditional database update is the concurrency boundary", app, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingCandidateStatus.Rejected", app, StringComparison.Ordinal);
        Assert.Contains("Converting = 4", Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "Enums", "CrmProspectingCandidateStatus.cs"), StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync", app, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmLead)", app, StringComparison.Ordinal);
        Assert.Contains("leadDomain.CreateAsync", app, StringComparison.Ordinal);
        Assert.Contains("GetRunCandidatesAsync", app, StringComparison.Ordinal);
        Assert.Contains("ReviewCandidateAsync", app, StringComparison.Ordinal);
        Assert.Contains("ConvertApprovedCandidateToLeadAsync", app, StringComparison.Ordinal);
        Assert.Contains("seenCandidateEmails", worker, StringComparison.Ordinal);
        Assert.Contains("Run cancelled before candidate normalization", worker, StringComparison.Ordinal);
        Assert.Contains("RawResultsJson = rawResultsJson", repository, StringComparison.Ordinal);
        Assert.Contains("CandidateCount = candidateCount", repository, StringComparison.Ordinal);
        Assert.Contains("CandidateCount = candidateCount", repository, StringComparison.Ordinal);
    }


    [Fact]
    public void CandidateConversion_ShouldUseAtomicConditionalRepositoryClaim()
    {
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Repositories", "ICrmProspectingCandidateRepository.cs");
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingCandidateRepository.cs");
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmProspectingAppService.cs");
        Assert.Contains("TryClaimForConversionAsync", contract, StringComparison.Ordinal);
        Assert.Contains("candidate.Status == CrmProspectingCandidateStatus.Approved", repository, StringComparison.Ordinal);
        Assert.Contains("Status = CrmProspectingCandidateStatus.Converting", repository, StringComparison.Ordinal);
        Assert.Contains("TryClaimForConversionAsync(candidate.BasicId", app, StringComparison.Ordinal);
        Assert.Contains("already being converted", app, StringComparison.Ordinal);
        Assert.Contains("leads.GetByIdAsync(candidate.ConvertedLeadId.Value", app, StringComparison.Ordinal);
        Assert.Contains("Converted prospecting candidate is missing its Lead reference.", app, StringComparison.Ordinal);
        Assert.Contains("candidate.Status = CrmProspectingCandidateStatus.Converted", app, StringComparison.Ordinal);
    }

    [Fact]
    public void StrategyApproval_ShouldUseAtomicConditionalStateTransitions()
    {
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Repositories", "ICrmProspectingStrategyRepository.cs");
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingStrategyRepository.cs");
        var domain = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "DomainServices", "Implementations", "CrmProspectingDomainService.cs");
        Assert.Contains("TryTransitionApprovalAsync", contract, StringComparison.Ordinal);
        Assert.Contains("strategy.ApprovalStatus == expectedStatus", repository, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingStrategyApprovalStatus.Draft", domain, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingStrategyApprovalStatus.PendingApproval", domain, StringComparison.Ordinal);
        Assert.Contains("authenticated approver ID is required", domain, StringComparison.Ordinal);
        Assert.Contains("The strategy approval state changed", domain, StringComparison.Ordinal);
    }

    [Fact]
    public void StrategyLifecycle_ShouldSerializeVersionAllocationAndActivation()
    {
        var campaignContract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Repositories", "ICrmProspectingCampaignRepository.cs");
        var campaignRepository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingCampaignRepository.cs");
        var strategyContract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Repositories", "ICrmProspectingStrategyRepository.cs");
        var strategyRepository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingStrategyRepository.cs");
        var domain = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "DomainServices", "Implementations", "CrmProspectingDomainService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.17", "5.9.17.sql");

        Assert.Contains("TryAdvanceStrategyVersionAsync", campaignContract, StringComparison.Ordinal);
        Assert.Contains("campaign.StrategyVersionSequence == expectedSequence", campaignRepository, StringComparison.Ordinal);
        Assert.Contains("TrySetActiveStrategyVersionAsync", campaignContract, StringComparison.Ordinal);
        Assert.Contains("campaign.ActiveStrategyVersion == expectedVersion", campaignRepository, StringComparison.Ordinal);
        Assert.Contains("TryMarkActivatedAsync", strategyContract, StringComparison.Ordinal);
        Assert.Contains("ClearOtherActiveVersionsAsync", strategyRepository, StringComparison.Ordinal);
        Assert.Contains("System.Data.IsolationLevel.ReadCommitted", Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmProspectingAppService.cs"), StringComparison.Ordinal);
        Assert.Contains("TryAdvanceStrategyVersionAsync(campaign.BasicId", domain, StringComparison.Ordinal);
        Assert.Contains("TrySetActiveStrategyVersionAsync(campaign.BasicId", domain, StringComparison.Ordinal);
        Assert.Contains("ClearOtherActiveVersionsAsync(campaign.BasicId", domain, StringComparison.Ordinal);
        Assert.Contains("strategy_version_sequence", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateReview_ShouldUseAtomicConditionalStateTransition()
    {
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Repositories", "ICrmProspectingCandidateRepository.cs");
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingCandidateRepository.cs");
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmProspectingAppService.cs");
        Assert.Contains("TryReviewAsync", contract, StringComparison.Ordinal);
        Assert.Contains("candidate.Status == CrmProspectingCandidateStatus.PendingReview", repository, StringComparison.Ordinal);
        Assert.Contains("candidate.Status == CrmProspectingCandidateStatus.Duplicate", repository, StringComparison.Ordinal);
        Assert.Contains("(!approve || overrideDuplicate)", repository, StringComparison.Ordinal);
        Assert.Contains("TryReviewAsync(candidate.BasicId, input.Approve, input.OverrideDuplicate", app, StringComparison.Ordinal);
        Assert.Contains("already reviewed or its review state changed", app, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
