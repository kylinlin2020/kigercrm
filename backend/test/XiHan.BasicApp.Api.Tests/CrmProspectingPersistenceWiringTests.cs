using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level guards for the first prospecting persistence slice.</summary>
public sealed class CrmProspectingPersistenceWiringTests
{
    [Theory]
    [InlineData("SysCrmProspectingCampaign.cs", "Crm_Prospecting_Campaign", "OwnerUserId")]
    [InlineData("SysCrmProspectingStrategy.cs", "Crm_Prospecting_Strategy", "CampaignId")]
    public void MutableProspectingEntities_ShouldBeAuditedAndStrictlyTenantScoped(string fileName, string tableName, string indexedProperty)
    {
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", fileName);

        Assert.Contains("BasicAppFullAuditedEntity", entity, StringComparison.Ordinal);
        Assert.Contains("IStrictMultiTenantEntity", entity, StringComparison.Ordinal);
        Assert.Contains($"TableName = \"{tableName}\"", entity, StringComparison.Ordinal);
        Assert.Contains($"nameof({indexedProperty})", entity, StringComparison.Ordinal);
        Assert.Contains("IX_{table}_TeId_CrTi", entity, StringComparison.Ordinal);
        Assert.Contains("IX_{table}_CrId", entity, StringComparison.Ordinal);
        Assert.Contains("IX_{table}_TeId_IsDe", entity, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingCandidate_ShouldBeAuditedTenantScopedAndSeparateFromLead()
    {
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingCandidate.cs");
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingCandidateRepository.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.14", "5.9.14.sql");
        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        Assert.Contains("BasicAppFullAuditedEntity", entity, StringComparison.Ordinal);
        Assert.Contains("IStrictMultiTenantEntity", entity, StringComparison.Ordinal);
        Assert.Contains("PendingReview", entity, StringComparison.Ordinal);
        Assert.Contains("Converted_Lead_Id", entity, StringComparison.Ordinal);
        Assert.Contains("SaasRepository<SysCrmProspectingCandidate>", repository, StringComparison.Ordinal);
        Assert.Contains("crm_prospecting_candidate", migration, StringComparison.Ordinal);
        Assert.Contains("crm_lead", migration, StringComparison.Ordinal);
        Assert.Contains(".Add<SysCrmProspectingCandidate>()", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingRun_ShouldBePermanentCreationOnlyIdempotencyLedger()
    {
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingRun.cs");
        Assert.Contains("BasicAppCreationEntity", entity, StringComparison.Ordinal);
        Assert.Contains("IStrictMultiTenantEntity", entity, StringComparison.Ordinal);
        Assert.Contains("UX_{table}_Te_Rq", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDeleted", entity, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunStatus", entity, StringComparison.Ordinal);
        Assert.Contains("Snapshot_Search_Objective", entity, StringComparison.Ordinal);
        Assert.Contains("Snapshot_Provider_Plan_Json", entity, StringComparison.Ordinal);
        Assert.Contains("Snapshot_Excluded_Domains_Json", entity, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingRepositories_ShouldUseSaasRepositoryBase()
    {
        foreach (var name in new[] { "Campaign", "Strategy", "Run", "Candidate" })
        {
            var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", $"CrmProspecting{name}Repository.cs");
            Assert.Contains("SaasRepository<", source, StringComparison.Ordinal);
            Assert.Contains($"ICrmProspecting{name}Repository", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProspectingEntities_ShouldBeRegisteredForFieldSecurity()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        Assert.Contains(".Add<SysCrmProspectingCampaign>()", source, StringComparison.Ordinal);
        Assert.Contains(".Add<SysCrmProspectingStrategy>()", source, StringComparison.Ordinal);
        Assert.Contains(".Add<SysCrmProspectingRun>()", source, StringComparison.Ordinal);
        Assert.Contains(".Add<SysCrmProspectingCandidate>()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingMigration_ShouldBeTenantDatabaseOnlyAndMatchEntityIndexes()
    {
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.11", "5.9.11.sql");
        Assert.Contains("IF to_regclass('crm_lead') IS NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_prospecting_campaign", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_prospecting_strategy", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_prospecting_run", migration, StringComparison.Ordinal);
        Assert.Contains("ux_crm_prospecting_strategy_te_ca_ve_isde", migration, StringComparison.Ordinal);
        Assert.Contains("ux_crm_prospecting_run_te_rq", migration, StringComparison.Ordinal);
        Assert.Contains("(tenant_id, campaign_id, version, is_deleted)", migration, StringComparison.Ordinal);
        Assert.Contains("(tenant_id, request_key)", migration, StringComparison.Ordinal);
        Assert.Contains("Runs are permanent execution/idempotency records", migration, StringComparison.Ordinal);
        Assert.Contains("ix_crm_prospecting_campaign_teid_crti", migration, StringComparison.Ordinal);
        Assert.Contains("ix_crm_prospecting_strategy_teid_isde", migration, StringComparison.Ordinal);
        Assert.Contains("ix_crm_prospecting_run_crid", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("is_deleted boolean", migration[migration.IndexOf("CREATE TABLE IF NOT EXISTS crm_prospecting_run", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }


    [Fact]
    public void ProspectingCandidateIdentity_ShouldBeUniquePerTenantRunProviderAndExternalId()
    {
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.14", "5.9.14.sql");

        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_candidate_te_ru_pr_ex_is", migration, StringComparison.Ordinal);
        Assert.Contains("ON crm_prospecting_candidate (tenant_id, run_id, provider_code, external_id, is_deleted)", migration, StringComparison.Ordinal);
        Assert.Contains("provider_code varchar(80) NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("external_id varchar(200) NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("is_deleted boolean NOT NULL DEFAULT false", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingRunRepository_ShouldGuardTerminalTransitionsAndDocumentSingleWorkerRecovery()
    {
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Repositories", "ICrmProspectingRunRepository.cs");

        Assert.Contains("run => run.BasicId == runId && run.Status == CrmProspectingRunStatus.Running", repository, StringComparison.Ordinal);
        Assert.Contains("run => run.BasicId == runId && run.Status == CrmProspectingRunStatus.CancelRequested", repository, StringComparison.Ordinal);
        Assert.Contains("run => run.Status == CrmProspectingRunStatus.Running", repository, StringComparison.Ordinal);
        Assert.Contains("Do not run concurrently with live workers; multi-instance recovery requires leases/fencing", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingWorker_ShouldPersistCandidateAndEvidenceInOneExplicitUnitOfWork()
    {
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");

        Assert.Contains("IUnitOfWorkManager", worker, StringComparison.Ordinal);
        Assert.Contains("Begin(new XiHanUnitOfWorkOptions(isTransactional: true))", worker, StringComparison.Ordinal);
        Assert.Contains("var insertedCandidate = await candidates.AddAsync(candidate, cancellationToken)", worker, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingEvidenceFactory.Create(", worker, StringComparison.Ordinal);
        Assert.Contains("await evidenceRepository.AddRangeAsync(evidenceRows, cancellationToken)", worker, StringComparison.Ordinal);
        Assert.Contains("await unitOfWork.CompleteAsync(cancellationToken)", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingWorker_ShouldReconcileMissingEvidenceForExistingCandidates()
    {
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");

        Assert.Contains("existingCandidateByKey.TryGetValue(key, out var existingCandidate)", worker, StringComparison.Ordinal);
        Assert.Contains(".ToHashSet(StringComparer.Ordinal)", worker, StringComparison.Ordinal);
        Assert.Contains(".ToDictionary(candidate => (candidate.ProviderCode, candidate.ExternalId))", worker, StringComparison.Ordinal);
        Assert.Contains(".Select(candidate => (candidate.ProviderCode, candidate.ExternalId))", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("$\"{candidate.ProviderCode}::{candidate.ExternalId}\"", worker, StringComparison.Ordinal);
        Assert.Contains("StringComparer.Ordinal);", worker, StringComparison.Ordinal);
        Assert.Contains("evidenceRepository.GetByCandidateAsync(", worker, StringComparison.Ordinal);
        Assert.Contains("evidence.ProviderRecordId,", worker, StringComparison.Ordinal);
        Assert.Contains(".ToHashSet();", worker, StringComparison.Ordinal);
        Assert.Contains("var missingEvidence = expectedEvidence", worker, StringComparison.Ordinal);
        Assert.Contains("await evidenceRepository.AddRangeAsync(missingEvidence, cancellationToken)", worker, StringComparison.Ordinal);
        Assert.Contains("await repairUnitOfWork.CompleteAsync(cancellationToken)", worker, StringComparison.Ordinal);
        Assert.Contains("existingCandidateByKey[key] = insertedCandidate", worker, StringComparison.Ordinal);
        Assert.Contains(".ToHashSet(StringComparer.Ordinal);", worker, StringComparison.Ordinal);

        var insertIndex = worker.IndexOf("var insertedCandidate = await candidates.AddAsync(candidate, cancellationToken)", StringComparison.Ordinal);
        var commitIndex = worker.IndexOf("await unitOfWork.CompleteAsync(cancellationToken)", insertIndex, StringComparison.Ordinal);
        var identityMapIndex = worker.IndexOf("existingCandidateByKey[key] = insertedCandidate", commitIndex, StringComparison.Ordinal);
        Assert.True(insertIndex >= 0 && commitIndex > insertIndex && identityMapIndex > commitIndex,
            "The persisted Candidate must be added to the in-memory identity map only after its transaction commits.");
    }

    [Fact]
    public void ProspectingCandidateStatusMigration_ShouldPreservePreviouslyConvertedRows()
    {
        var status = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "Enums", "CrmProspectingCandidateStatus.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.16", "5.9.16.sql");
        Assert.Contains("Converting = 4", status, StringComparison.Ordinal);
        Assert.Contains("Converted = 5", status, StringComparison.Ordinal);
        Assert.Contains("SET status = 5", migration, StringComparison.Ordinal);
        Assert.Contains("WHERE status = 4", migration, StringComparison.Ordinal);
        Assert.Contains("to_regclass('crm_prospecting_candidate')", migration, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
