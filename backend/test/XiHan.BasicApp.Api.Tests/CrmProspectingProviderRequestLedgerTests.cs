namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level contract guards for the provider request-attempt ledger schema.</summary>
public sealed class CrmProspectingProviderRequestLedgerTests
{
    [Fact]
    public void ProviderRequestEntity_ShouldBeAuditedStrictTenantScopedAndAppendOnly()
    {
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingProviderRequest.cs");
        Assert.Contains("BasicAppCreationEntity", entity, StringComparison.Ordinal);
        Assert.Contains("IStrictMultiTenantEntity", entity, StringComparison.Ordinal);
        Assert.Contains("Crm_Prospecting_Provider_Request", entity, StringComparison.Ordinal);
        Assert.Contains("UX_{table}_Te_Ru_Pr_At", entity, StringComparison.Ordinal);
        Assert.Contains("Request_Key", entity, StringComparison.Ordinal);
        Assert.Contains("Failure_Summary", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDeleted", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("Raw_Response", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", entity, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderRequestMigration_ShouldBeTenantDatabaseOnlyAndConstrainAttemptState()
    {
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.31", "5.9.31.sql");
        Assert.Contains("IF to_regclass('crm_prospecting_run') IS NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_prospecting_provider_request", migration, StringComparison.Ordinal);
        Assert.Contains("row_version bigint NOT NULL DEFAULT 0", migration, StringComparison.Ordinal);
        Assert.Contains("(tenant_id, run_id, provider_code, attempt_no)", migration, StringComparison.Ordinal);
        Assert.Contains("attempt_no > 0", migration, StringComparison.Ordinal);
        Assert.Contains("state IN ('Started','Succeeded','Failed','RateLimited','Cancelled')", migration, StringComparison.Ordinal);
        Assert.Contains("failure_summary varchar(1000)", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("raw_response", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderRequestEntity_ShouldBeRegisteredForFieldSecurityMetadata()
    {
        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        Assert.Contains(".Add<SysCrmProspectingProviderRequest>()", registration, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ICrmProspectingProviderRequestRepository, CrmProspectingProviderRequestRepository>()", registration, StringComparison.Ordinal);

        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");
        Assert.Contains("CreateStartedAsync", worker, StringComparison.Ordinal);
        Assert.Contains("TryFinalizeAsync", worker, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRequestFingerprint.Create", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderRequestLedger_ShouldBeRegisteredAndBracketWorkerProviderCalls()
    {
        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");
        Assert.Contains("ICrmProspectingProviderRequestRepository, CrmProspectingProviderRequestRepository", registration, StringComparison.Ordinal);
        Assert.Contains("CreateStartedAsync(", worker, StringComparison.Ordinal);
        Assert.Contains("provider.SearchAsync(request, cancellationToken)", worker, StringComparison.Ordinal);
        Assert.Contains("TryFinalizeAsync(", worker, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingProviderRateLimitException", worker, StringComparison.Ordinal);
        Assert.Contains("\"RateLimited\"", worker, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRequestFingerprint.Create(providerCode, request)", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleRequestRecovery_ShouldRequireExpiredOrTerminalRunAndBeInvokedAtStartup()
    {
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingProviderRequestRepository.cs");
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");
        Assert.Contains("request.state = 'Started'", repository, StringComparison.Ordinal);
        Assert.Contains("run.lease_expires_at <= recovery_clock.now", repository, StringComparison.Ordinal);
        Assert.Contains("worker_interrupted", repository, StringComparison.Ordinal);
        Assert.Contains("RecoverStaleStartedAsync(", worker, StringComparison.Ordinal);
        Assert.Contains("RunLeaseDuration, cancellationToken", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderRequestLedger_ShouldRecoverOnlyStaleAttemptsWhoseRunIsNoLongerLive()
    {
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingProviderRequestRepository.cs");
        Assert.Contains("RecoverStaleStartedAsync", repository, StringComparison.Ordinal);
        Assert.Contains("request.tenant_id = @tenantId", repository, StringComparison.Ordinal);
        Assert.Contains("request.state = 'Started'", repository, StringComparison.Ordinal);
        Assert.Contains("run.lease_expires_at <= recovery_clock.now", repository, StringComparison.Ordinal);
        Assert.Contains("worker_interrupted", repository, StringComparison.Ordinal);

        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");
        Assert.Contains("RecoverStaleStartedAsync", worker, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMinutes(15)", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderRequestLedger_ShouldPersistHttpStatusAndRetryAfterForThrottledProviders()
    {
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");
        Assert.Contains("CrmProspectingProviderRateLimitException", worker, StringComparison.Ordinal);
        Assert.Contains("httpStatus: ex.HttpStatus", worker, StringComparison.Ordinal);
        Assert.Contains("retryAfterAt: ex.RetryAfterAt", worker, StringComparison.Ordinal);
        Assert.Contains("httpFailure.StatusCode", worker, StringComparison.Ordinal);

        foreach (var providerFile in new[]
        {
            "CrmTedProspectingProvider.cs",
            "CrmFindTenderProspectingProvider.cs",
            "CrmContractsFinderProspectingProvider.cs",
            "CrmUsaSpendingProspectingProvider.cs"
        })
        {
            var provider = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", providerFile);
            Assert.Contains("CrmProspectingProviderRateLimitException", provider, StringComparison.Ordinal);
        }
    }

    private static string Read(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "src", "modules", "XiHan.BasicApp.Crm", "XiHan.BasicApp.Crm.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
