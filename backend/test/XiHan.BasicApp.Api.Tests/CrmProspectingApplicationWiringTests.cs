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
        foreach (var operation in new[] { "CreateCampaignAsync", "UpdateCampaignAsync", "GetCampaignAsync", "CreateStrategyVersionAsync", "SubmitStrategyForApprovalAsync", "DecideStrategyAsync", "ActivateStrategyAsync", "GetStrategiesAsync", "GetCampaignRunsAsync", "StartRunAsync", "GetRunAsync", "CancelRunAsync" })
        {
            Assert.Contains(operation, contract, StringComparison.Ordinal);
            Assert.Contains(operation, service, StringComparison.Ordinal);
        }
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Update)]", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", service, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(x => x.Version)", service, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(x => x.CreatedTime)", service, StringComparison.Ordinal);
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
        Assert.Contains("AddSingleton<ICrmProspectingProvider, FakeCrmProspectingProvider>()", registration, StringComparison.Ordinal);
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
        Assert.Contains("RawResultsJson = rawResultsJson", repository, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunStatus.Succeeded", repository, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunStatus.PartiallySucceeded", repository, StringComparison.Ordinal);
        Assert.Contains("OnCompleted", app, StringComparison.Ordinal);
        Assert.Contains("ICrmProspectingProvider", worker, StringComparison.Ordinal);
        Assert.Contains("ITenantDataScopeRunner", worker, StringComparison.Ordinal);
        Assert.Contains("ProviderPlanJson", worker, StringComparison.Ordinal);
        Assert.Contains("CandidateCount = candidateCount", repository, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS raw_results_json text", migration, StringComparison.Ordinal);
        Assert.Contains("Raw result cap reached", worker, StringComparison.Ordinal);
        Assert.Contains("5_000_000", worker, StringComparison.Ordinal);
        Assert.Contains("PendingReview", candidate, StringComparison.Ordinal);
        Assert.Contains("DuplicateLeadId", candidate, StringComparison.Ordinal);
        Assert.Contains("CrmLeadDuplicateMatcher.NormalizeEmail", worker, StringComparison.Ordinal);
        Assert.Contains("lead.WebsiteHost == websiteHost", worker, StringComparison.Ordinal);
        Assert.Contains("crm_prospecting_candidate", candidateMigration, StringComparison.Ordinal);
        Assert.Contains("CandidateCount = candidateCount", Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs"), StringComparison.Ordinal);
        Assert.Contains("GetRunCandidatesAsync", app, StringComparison.Ordinal);
        Assert.Contains("ReviewCandidateAsync", app, StringComparison.Ordinal);
        Assert.Contains("Duplicate candidate approval requires an explicit duplicate override", app, StringComparison.Ordinal);
        Assert.Contains("ConvertApprovedCandidateToLeadAsync", app, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingCandidateStatus.Approved", app, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingCandidateStatus.Rejected", app, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync", app, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatableAsync(typeof(SysCrmLead)", app, StringComparison.Ordinal);
        Assert.Contains("leadDomain.CreateAsync", app, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
