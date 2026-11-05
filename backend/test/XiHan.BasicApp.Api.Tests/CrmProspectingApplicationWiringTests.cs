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
        Assert.Contains("EnableWorldBankProcurementProvider", registration, StringComparison.Ordinal);
        Assert.Contains("CrmWorldBankProcurementProvider", registration, StringComparison.Ordinal);
        Assert.Contains("search.worldbank.org", registration, StringComparison.Ordinal);
        Assert.Contains("EnableUnComtradeProvider", registration, StringComparison.Ordinal);
        Assert.Contains("CrmUnComtradeProspectingProvider", registration, StringComparison.Ordinal);
        Assert.Contains("comtradeapi.un.org", registration, StringComparison.Ordinal);
        Assert.Contains("EnableUsCensusTradeProvider", registration, StringComparison.Ordinal);
        Assert.Contains("CrmUsCensusTradeProspectingProvider", registration, StringComparison.Ordinal);
        Assert.Contains("api.census.gov", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSingleton<ICrmProspectingProvider, FakeCrmProspectingProvider>();", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void TradeProviders_ShouldResolveEncryptedCredentialsInsideTheCurrentTenantScope()
    {
        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");
        var comtrade = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmUnComtradeProspectingProvider.cs");
        var census = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmUsCensusTradeProspectingProvider.cs");
        var frontend = Read("frontend", "src", "modules", "crm", "views", "crm", "prospecting", "ProviderCredentialsPanel.vue");

        Assert.Contains("services.AddScoped<CrmUnComtradeProspectingProvider>", registration, StringComparison.Ordinal);
        Assert.Contains("services.AddScoped<CrmUsCensusTradeProspectingProvider>", registration, StringComparison.Ordinal);
        Assert.Contains("ISaasConfigurationService", registration, StringComparison.Ordinal);
        Assert.Contains("ICurrentTenant>().Change(message.TenantId)", worker, StringComparison.Ordinal);
        Assert.Contains("CRM.Prospecting.Provider.un_comtrade.ApiKey", comtrade, StringComparison.Ordinal);
        Assert.Contains("CRM.Prospecting.Provider.us_census.ApiKey", census, StringComparison.Ordinal);
        Assert.Contains("Ocp-Apim-Subscription-Key", comtrade, StringComparison.Ordinal);
        Assert.Contains("ConfigType.Tenant", frontend, StringComparison.Ordinal);
        Assert.Contains("isEncrypted: true", frontend, StringComparison.Ordinal);
        Assert.Contains("CRM.Prospecting.Provider.${item.code}.ApiKey", frontend, StringComparison.Ordinal);
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
    public void CandidateConversion_ShouldBeTransactionalAndReturnExistingLeadOnRetry()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmProspectingAppService.cs");
        var methodIndex = app.IndexOf("public async Task<CrmLeadDetailDto> ConvertApprovedCandidateToLeadAsync", StringComparison.Ordinal);
        Assert.True(methodIndex > 0, "Candidate conversion application method must exist.");

        var methodStart = app.LastIndexOf("[UnitOfWork(true)]", methodIndex, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "Candidate conversion must run inside an explicit transactional UnitOfWork.");

        var methodEnd = app.IndexOf("\n    private static CrmProspectingCandidateDto", methodIndex, StringComparison.Ordinal);
        Assert.True(methodEnd > methodIndex, "Candidate conversion method boundary must be found.");
        var method = app[methodIndex..methodEnd];

        var retryBranch = method.IndexOf("candidate.Status == CrmProspectingCandidateStatus.Converted", StringComparison.Ordinal);
        var existingLeadRead = method.IndexOf("leads.GetByIdAsync(candidate.ConvertedLeadId.Value", StringComparison.Ordinal);
        var claim = method.IndexOf("candidates.TryClaimForConversionAsync(candidate.BasicId", StringComparison.Ordinal);
        var leadCreate = method.IndexOf("leadDomain.CreateAsync", StringComparison.Ordinal);
        var candidateFinalize = method.IndexOf("candidate.Status = CrmProspectingCandidateStatus.Converted", StringComparison.Ordinal);
        var candidateSave = method.IndexOf("candidates.UpdateAsync(candidate", StringComparison.Ordinal);

        Assert.True(retryBranch >= 0 && existingLeadRead > retryBranch && claim > existingLeadRead,
            "A retry after commit must return the existing Lead before attempting a new conversion claim.");
        Assert.True(claim >= 0 && leadCreate > claim && candidateFinalize > leadCreate && candidateSave > candidateFinalize,
            "Conversion must claim, create the Lead, then persist the Converted state and Lead reference.");
        Assert.Contains("Converted prospecting candidate is missing its Lead reference.", method, StringComparison.Ordinal);
        Assert.Contains("Converted prospecting candidate references a missing Lead.", method, StringComparison.Ordinal);
        Assert.Contains("The conditional database update is the concurrency boundary", method, StringComparison.Ordinal);
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
        Assert.Contains("strategy.ActivatedAt == null", strategyRepository, StringComparison.Ordinal);
        Assert.Contains("ClearOtherActiveVersionsAsync", strategyRepository, StringComparison.Ordinal);
        Assert.Contains("System.Data.IsolationLevel.ReadCommitted", Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmProspectingAppService.cs"), StringComparison.Ordinal);
        Assert.Contains("TryAdvanceStrategyVersionAsync(campaign.BasicId", domain, StringComparison.Ordinal);
        Assert.Contains("TrySetActiveStrategyVersionAsync(campaign.BasicId", domain, StringComparison.Ordinal);
        Assert.Contains("campaign.ActiveStrategyVersion == strategy.Version && strategy.ActivatedAt != null", domain, StringComparison.Ordinal);
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


    [Fact]
    public void ProspectingWorker_ShouldKeepSingleInstanceRestrictionUntilLeaseFencingExists()
    {
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        var runEntity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingRun.cs");
        var runbook = Read("docs", "kiger", "PROSPECTING-WORKER-OPERATIONS.md");

        Assert.Contains("await RecoverPendingAsync(stoppingToken);", worker, StringComparison.Ordinal);
        Assert.Contains("await base.ExecuteAsync(stoppingToken);", worker, StringComparison.Ordinal);
        Assert.Contains("run.Status == CrmProspectingRunStatus.Running", repository, StringComparison.Ordinal);
        Assert.Contains("Status = CrmProspectingRunStatus.Queued", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("LeaseOwnerId", runEntity, StringComparison.Ordinal);
        Assert.DoesNotContain("FencingToken", runEntity, StringComparison.Ordinal);
        Assert.Contains("Run exactly one active application process", runbook, StringComparison.Ordinal);
        Assert.Contains("Gate before enabling multiple workers", runbook, StringComparison.Ordinal);
    }


    [Fact]
    public void CandidateReviewDetail_ShouldExposeAuthorizedEvidenceAndScoreHistory()
    {
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Contracts", "ICrmProspectingAppService.cs");
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmProspectingAppService.cs");
        var dtos = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmProspectingDtos.cs");
        var evidenceRepo = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingEvidenceRepository.cs");
        var scoreRepo = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingScoreRepository.cs");

        Assert.Contains("GetCandidateReviewDetailAsync", contract, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", app, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId", app, StringComparison.Ordinal);
        Assert.Contains("evidence.GetByCandidateAsync(candidate.BasicId", app, StringComparison.Ordinal);
        Assert.Contains("scores.GetLatestByCandidateAsync(candidate.BasicId, 20", app, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingCandidateReviewDetailDto", dtos, StringComparison.Ordinal);
        Assert.Contains("VerificationStatus", dtos, StringComparison.Ordinal);
        Assert.Contains("ScorecardVersion", dtos, StringComparison.Ordinal);
        Assert.Contains("CreateQueryable()", evidenceRepo, StringComparison.Ordinal);
        Assert.Contains("CreateQueryable()", scoreRepo, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingWorker_ShouldIsolateMalformedProviderRegistrationsAndPayloads()
    {
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");

        Assert.Contains("!string.IsNullOrWhiteSpace(provider.ProviderCode)", worker, StringComparison.Ordinal);
        Assert.Contains("provider.ProviderCode.Trim().Length <= 80", worker, StringComparison.Ordinal);
        Assert.Contains(".GroupBy(provider => provider.ProviderCode.Trim(), StringComparer.OrdinalIgnoreCase)", worker, StringComparison.Ordinal);
        Assert.Contains("if (result is null || result.Records is null)", worker, StringComparison.Ordinal);
        Assert.Contains("provider returned an empty or malformed result envelope", worker, StringComparison.Ordinal);
        Assert.Contains("if (record is null)", worker, StringComparison.Ordinal);
        Assert.Contains("provider returned a null record; it was skipped", worker, StringComparison.Ordinal);
        Assert.Contains("new CrmProspectingRawEnvelope(provider.ProviderCode.Trim(), record)", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingWorker_ShouldBoundUntrustedRawRecordsBeforeRetention()
    {
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");
        var identity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Prospecting", "CrmProspectingRecordIdentity.cs");
        var evidence = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Prospecting", "CrmProspectingEvidenceFactory.cs");

        Assert.Contains("IsWithinRawRecordLimits(record, out var estimatedRecordBytes)", worker, StringComparison.Ordinal);
        Assert.Contains("maxRawRecordJsonBytes = 64_000", worker, StringComparison.Ordinal);
        Assert.Contains("maxRawJsonBytes = 5_000_000", worker, StringComparison.Ordinal);
        Assert.Contains("oversized provider record was rejected before retention", worker, StringComparison.Ordinal);
        Assert.Contains("characters * 6", worker, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRecordIdentity.NormalizeExternalId(record)", worker, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRecordIdentity.NormalizeExternalId(record)", evidence, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRecordIdentity.GetPayloadHash(record)", evidence, StringComparison.Ordinal);
        Assert.Contains("SerializeToUtf8Bytes(record, CanonicalJsonOptions)", identity, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingWorker_ShouldPersistConservativeScoreWithCandidateAndEvidence()
    {
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");

        Assert.Contains("GetRequiredService<ICrmProspectingScoreRepository>()", worker, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingScorecard.Evaluate(scoreInput, evaluatedAtUtc)", worker, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingScoreSnapshotFactory.Create(insertedCandidate.BasicId, scoreResult)", worker, StringComparison.Ordinal);
        Assert.Contains("HasDirectProcurementSignal: false", worker, StringComparison.Ordinal);
        Assert.Contains("HasDirectProductFit: false", worker, StringComparison.Ordinal);
        Assert.Contains("HasTargetMarketFit: false", worker, StringComparison.Ordinal);
        Assert.Contains("await unitOfWork.CompleteAsync(cancellationToken)", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingWorker_ShouldStripControlCharactersFromUntrustedWarnings()
    {
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");

        Assert.Contains("char.IsControl(character) ? ' ' : character", worker, StringComparison.Ordinal);
        Assert.Contains("if (value.Length == 0) return;", worker, StringComparison.Ordinal);
        Assert.Contains("MaxAggregatedWarningLength = 500", worker, StringComparison.Ordinal);
        Assert.Contains("MaxAggregatedWarnings = 100", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderPlanValidation_ShouldApplyBeforeStrategyPersistenceAndAgainBeforeRunCreation()
    {
        var domain = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain",
            "DomainServices", "Implementations", "CrmProspectingDomainService.cs");
        Assert.Contains("ValidateProviderPlan(strategy.ProviderPlanJson)", domain, StringComparison.Ordinal);
        Assert.Contains("private static void ValidateProviderPlan(string json)", domain, StringComparison.Ordinal);
        Assert.Contains("MaxProviderPlanEntries = 32", domain, StringComparison.Ordinal);
        Assert.Contains("MaxProviderCodeLength = 80", domain, StringComparison.Ordinal);
        Assert.Contains("StringComparer.OrdinalIgnoreCase", domain, StringComparison.Ordinal);
        Assert.Contains("providerCode.Any(char.IsControl)", domain, StringComparison.Ordinal);

        var strategyCreation = domain.IndexOf("public async Task<SysCrmProspectingStrategy> CreateStrategyVersionAsync", StringComparison.Ordinal);
        var runCreation = domain.IndexOf("public async Task<SysCrmProspectingRun> CreateRunAsync", StringComparison.Ordinal);
        Assert.True(strategyCreation >= 0 && runCreation > strategyCreation);
        Assert.True(domain.IndexOf("ValidateStrategy(strategy)", strategyCreation, StringComparison.Ordinal) <
                    domain.IndexOf("campaigns.GetByIdAsync(strategy.CampaignId", strategyCreation, StringComparison.Ordinal),
            "Invalid provider plans must be rejected before campaign reads or version allocation.");
        Assert.True(domain.IndexOf("ValidateProviderPlan(strategy.ProviderPlanJson)", runCreation, StringComparison.Ordinal) > runCreation,
            "Run creation must revalidate persisted plans before snapshotting them.");
    }

    [Fact]
    public void ProspectingRunCreation_ShouldRecoverOnlyTheExactRequestKeyUniqueViolationAfterRollback()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "AppServices", "CrmProspectingAppService.cs");
        var domain = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Domain", "DomainServices", "Implementations", "CrmProspectingDomainService.cs");

        var startIndex = app.IndexOf("public async Task<CrmProspectingRunDto> StartRunAsync", StringComparison.Ordinal);
        Assert.True(startIndex > 0);
        var startEnd = app.IndexOf("\n    private async Task<CrmProspectingRunDto> StartRunInUnitOfWorkAsync", startIndex, StringComparison.Ordinal);
        Assert.True(startEnd > startIndex);
        var startMethod = app[startIndex..startEnd];
        Assert.DoesNotContain("[UnitOfWork(true)]\n    [PermissionAuthorize(CrmPermissionCodes.Execute)]\n    public async Task<CrmProspectingRunDto> StartRunAsync", app, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex) when (IsRunRequestKeyUniqueViolation(ex))", startMethod, StringComparison.Ordinal);
        Assert.Contains("return await StartRunInUnitOfWorkAsync(input, cancellationToken)", startMethod, StringComparison.Ordinal);

        var helperIndex = app.IndexOf("private async Task<CrmProspectingRunDto> StartRunInUnitOfWorkAsync", startEnd, StringComparison.Ordinal);
        var detectorIndex = app.IndexOf("private static bool IsRunRequestKeyUniqueViolation", helperIndex, StringComparison.Ordinal);
        Assert.True(helperIndex > startEnd && detectorIndex > helperIndex);
        var helper = app[helperIndex..detectorIndex];
        Assert.Contains("using var uow = unitOfWorkManager.Begin(new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true)", helper, StringComparison.Ordinal);
        Assert.Contains("domain.CreateRunAsync(input.CampaignId, input.RequestKey, cancellationToken)", helper, StringComparison.Ordinal);
        Assert.Contains("uow.OnCompleted(() => runQueue.EnqueueAsync(message, TimeSpan.Zero))", helper, StringComparison.Ordinal);
        Assert.Contains("await uow.CompleteAsync(cancellationToken)", helper, StringComparison.Ordinal);

        var detectorEnd = app.IndexOf("\n    [PermissionAuthorize(CrmPermissionCodes.Read)]", detectorIndex, StringComparison.Ordinal);
        Assert.True(detectorEnd > detectorIndex);
        var detector = app[detectorIndex..detectorEnd];
        Assert.Contains("Npgsql.PostgresException", detector, StringComparison.Ordinal);
        Assert.Contains("23505", detector, StringComparison.Ordinal);
        Assert.Contains("ux_crm_prospecting_run_te_rq", detector, StringComparison.Ordinal);
        Assert.Contains("ConstraintName", detector, StringComparison.Ordinal);

        var domainCreateIndex = domain.IndexOf("public async Task<SysCrmProspectingRun> CreateRunAsync", StringComparison.Ordinal);
        var existingLookup = domain.IndexOf("runs.GetByRequestKeyAsync(requestKey, cancellationToken)", domainCreateIndex, StringComparison.Ordinal);
        var insert = domain.IndexOf("return await runs.AddAsync(run, cancellationToken)", domainCreateIndex, StringComparison.Ordinal);
        Assert.True(existingLookup > domainCreateIndex && insert > existingLookup,
            "The retry must encounter the tenant-scoped idempotency lookup before any second insert.");
        Assert.Contains("existing.PayloadHash, payloadHash", domain, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
