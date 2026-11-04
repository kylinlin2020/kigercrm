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
    public void ProspectingRepositories_ShouldBeExplicitlyRegisteredForWorkerAndApplicationServices()
    {
        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");

        foreach (var repository in new[]
        {
            "Campaign", "Strategy", "Run", "Candidate", "Evidence", "Score"
        })
        {
            Assert.Contains($"AddScoped<ICrmProspecting{repository}Repository, CrmProspecting{repository}Repository>()", registration, StringComparison.Ordinal);
        }

        Assert.Contains("GetRequiredService<ICrmProspectingScoreRepository>()", worker, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<ICrmProspectingEvidenceRepository>()", worker, StringComparison.Ordinal);
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
    public void ProspectingWorker_ShouldRemainSingleInstanceUntilDatabaseFencingIsImplemented()
    {
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");
        var operations = Read("docs", "kiger", "PROSPECTING-WORKER-OPERATIONS.md");
        var leaseDesign = Read("docs", "kiger", "PROSPECTING-WORKER-LEASE-DESIGN.md");

        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        Assert.Contains("XiHanBackgroundServiceBase<CrmProspectingRunHostedService>", worker, StringComparison.Ordinal);
        Assert.Contains("HostedService suffix", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("AddHostedService<CrmProspectingRunHostedService>", registration, StringComparison.Ordinal);
        Assert.Contains("config.UpdateMaxConcurrentTasks(1)", worker, StringComparison.Ordinal);
        Assert.Contains("await RecoverPendingAsync(stoppingToken)", worker, StringComparison.Ordinal);
        Assert.Contains("run => run.Status == CrmProspectingRunStatus.Running", worker, StringComparison.Ordinal);
        Assert.Contains("Run exactly one active application process", operations, StringComparison.Ordinal);
        Assert.Contains("implementation specification; not implemented", leaseDesign, StringComparison.Ordinal);
        Assert.Contains("SELECT ... FOR UPDATE", leaseDesign, StringComparison.Ordinal);
        Assert.Contains("lease_generation", leaseDesign, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingWorker_ShouldRepairMissingScoreSnapshotsWithEvidenceAtomically()
    {
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");

        Assert.Contains("GetLatestByCandidateAsync(", worker, StringComparison.Ordinal);
        Assert.Contains("var needsScoreRepair = existingScores.Count == 0", worker, StringComparison.Ordinal);
        Assert.Contains("var completeEvidence = persistedEvidence.Concat(missingEvidence).ToArray()", worker, StringComparison.Ordinal);
        Assert.Contains(".Take(32)", worker, StringComparison.Ordinal);
        Assert.Contains("existingCandidate.BasicId, scoreResult, scoreEvidence)", worker, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingScorecard.Evaluate(scoreInput, evaluatedAtUtc)", worker, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingScoreSnapshotFactory.Create(", worker, StringComparison.Ordinal);
        Assert.Contains("await scoreRepository.AddAsync(scoreSnapshot, cancellationToken)", worker, StringComparison.Ordinal);
        Assert.Contains("await repairUnitOfWork.CompleteAsync(cancellationToken)", worker, StringComparison.Ordinal);
        Assert.Contains("HasDirectProcurementSignal: false", worker, StringComparison.Ordinal);
        Assert.Contains("HasDirectProductFit: false", worker, StringComparison.Ordinal);
        Assert.Contains("HasTargetMarketFit: false", worker, StringComparison.Ordinal);
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

    [Fact]
    public void ProspectingEvidenceAndScoreIndexes_ShouldMatchCodeFirstNamesAfterUpgrade()
    {
        var evidence = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingEvidence.cs");
        var score = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingScore.cs");
        var initialEvidenceMigration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.21", "5.9.21.sql");
        var initialScoreMigration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.19", "5.9.19.sql");
        var alignmentMigration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.22", "5.9.22.sql");

        Assert.Contains("IX_{table}_Te_Ca_Fi", evidence, StringComparison.Ordinal);
        Assert.Contains("IX_{table}_Te_Pr_Ri", evidence, StringComparison.Ordinal);
        Assert.Contains("UX_{table}_EvidenceIdentity", evidence, StringComparison.Ordinal);
        Assert.Contains("IX_{table}_Te_Ca_Ev", score, StringComparison.Ordinal);
        Assert.Contains("IX_{table}_Te_Sv_Pr", score, StringComparison.Ordinal);

        Assert.Contains("ix_crm_prosp_evidence_tenant_candidate_field", initialEvidenceMigration, StringComparison.Ordinal);
        Assert.Contains("ix_crm_prosp_score_tenant_candidate_evaluated", initialScoreMigration, StringComparison.Ordinal);
        foreach (var legacyIndex in new[]
        {
            "ix_crm_prosp_evidence_tenant_candidate_field",
            "ix_crm_prosp_evidence_tenant_provider_retrieved",
            "ux_crm_prosp_evidence_identity",
            "ix_crm_prosp_score_tenant_candidate_evaluated",
            "ix_crm_prosp_score_tenant_version_priority"
        })
        {
            Assert.Contains($"DROP INDEX IF EXISTS {legacyIndex}", alignmentMigration, StringComparison.Ordinal);
        }

        foreach (var canonicalIndex in new[]
        {
            "ix_crm_prospecting_evidence_te_ca_fi",
            "ix_crm_prospecting_evidence_te_pr_ri",
            "ix_crm_prospecting_score_te_ca_ev",
            "ix_crm_prospecting_score_te_sv_pr"
        })
        {
            Assert.Contains($"CREATE INDEX IF NOT EXISTS {canonicalIndex}", alignmentMigration, StringComparison.Ordinal);
        }
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_evidence_evidenceidentity", alignmentMigration, StringComparison.Ordinal);

        Assert.Contains("IF to_regclass('crm_prospecting_evidence') IS NOT NULL", alignmentMigration, StringComparison.Ordinal);
        Assert.Contains("IF to_regclass('crm_prospecting_score') IS NOT NULL", alignmentMigration, StringComparison.Ordinal);
        Assert.Contains("ON crm_prospecting_evidence (tenant_id, candidate_id, field_name)", alignmentMigration, StringComparison.Ordinal);
        Assert.Contains("ON crm_prospecting_evidence (tenant_id, provider_code, retrieved_at_utc DESC)", alignmentMigration, StringComparison.Ordinal);
        Assert.Contains("ON crm_prospecting_evidence (tenant_id, candidate_id, provider_code, provider_record_id,", alignmentMigration, StringComparison.Ordinal);
        Assert.Contains("field_name, source_url_hash, raw_payload_hash", alignmentMigration, StringComparison.Ordinal);
        Assert.Contains("ON crm_prospecting_score (tenant_id, candidate_id, evaluated_at_utc DESC)", alignmentMigration, StringComparison.Ordinal);
        Assert.Contains("ON crm_prospecting_score (tenant_id, scorecard_version, priority DESC)", alignmentMigration, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_prospecting_evidence_evidenceidentity", alignmentMigration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingScoreEvidenceReferences_ShouldBePersistedAndMigrated()
    {
        var score = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingScore.cs");
        var factory = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Prospecting", "CrmProspectingScoreSnapshotFactory.cs");
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.23", "5.9.23.sql");

        Assert.Contains("Evidence_References_Json", score, StringComparison.Ordinal);
        Assert.Contains("EvidenceReferencesJson = evidenceReferencesJson", factory, StringComparison.Ordinal);
        Assert.Contains("OrderBy(row => row.ProviderCode, StringComparer.Ordinal)", factory, StringComparison.Ordinal);
        Assert.Contains("at most 100 evidence rows", factory, StringComparison.Ordinal);
        Assert.Contains("32 KB score snapshot limit", factory, StringComparison.Ordinal);
        Assert.Contains("scoreResult, evidenceRows", worker, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS evidence_references_json text NOT NULL DEFAULT '[]'", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingScoreMigration_ShouldGuardExistingRowsAndEnforceComponentArithmetic()
    {
        var createMigration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.19", "5.9.19.sql");
        var integrityMigration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.20", "5.9.20.sql");
        var scoreEntity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingScore.cs");
        var factory = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Prospecting", "CrmProspectingScoreSnapshotFactory.cs");

        foreach (var range in new[]
        {
            "total BETWEEN 0 AND 100",
            "priority BETWEEN 0 AND 3",
            "identity_points BETWEEN 0 AND 15",
            "procurement_points BETWEEN 0 AND 30",
            "product_fit_points BETWEEN 0 AND 20",
            "market_fit_points BETWEEN 0 AND 10",
            "recency_points BETWEEN 0 AND 10",
            "evidence_points BETWEEN 0 AND 10",
            "contact_points BETWEEN 0 AND 5"
        })
        {
            Assert.Contains(range, createMigration, StringComparison.Ordinal);
        }

        Assert.Contains("IF to_regclass('crm_prospecting_score') IS NOT NULL", integrityMigration, StringComparison.Ordinal);
        Assert.Contains("WHERE total <> identity_points + procurement_points + product_fit_points", integrityMigration, StringComparison.Ordinal);
        Assert.Contains("RAISE EXCEPTION", integrityMigration, StringComparison.Ordinal);
        Assert.Contains("ck_crm_prospecting_score_component_total", integrityMigration, StringComparison.Ordinal);
        Assert.Contains("total = identity_points + procurement_points + product_fit_points", integrityMigration, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT ck_crm_prospecting_score_component_total", integrityMigration, StringComparison.Ordinal);

        Assert.Contains("result.Total is < 0 or > 100", factory, StringComparison.Ordinal);
        Assert.Contains("componentTotal != result.Total", factory, StringComparison.Ordinal);
        Assert.Contains("result.IdentityPoints is < 0 or > 15", factory, StringComparison.Ordinal);
        Assert.Contains("result.ProcurementPoints is < 0 or > 30", factory, StringComparison.Ordinal);
        Assert.Contains("result.ContactPoints is < 0 or > 5", factory, StringComparison.Ordinal);
        Assert.Contains("IsNullable = false", scoreEntity, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
