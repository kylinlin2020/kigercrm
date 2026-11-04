namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level guards for immutable, tenant-scoped prospecting evidence metadata.</summary>
public sealed class CrmProspectingEvidenceWiringTests
{
    [Fact]
    public void EvidenceEntity_ShouldBeImmutableCreationOnlyAndStrictlyTenantScoped()
    {
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingEvidence.cs");
        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        Assert.Contains("BasicAppCreationEntity", entity, StringComparison.Ordinal);
        Assert.Contains("IStrictMultiTenantEntity", entity, StringComparison.Ordinal);
        Assert.Contains("FieldValueHash", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("BasicAppFullAuditedEntity", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("BusinessEmail", entity, StringComparison.Ordinal);
        Assert.Contains(".Add<SysCrmProspectingEvidence>()", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceMigration_ShouldBeTenantScopedAndRequireCandidateParent()
    {
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.18", "5.9.18.sql");
        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs");
        Assert.Contains("IF to_regclass('crm_prospecting_candidate') IS NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("tenant_id bigint NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY (tenant_id, candidate_id)", migration, StringComparison.Ordinal);
        Assert.Contains("ux_crm_prosp_candidate_tenant_basic", migration, StringComparison.Ordinal);
        Assert.Contains("(tenant_id, candidate_id, field_name)", migration, StringComparison.Ordinal);
        Assert.Contains("(tenant_id, provider_code, retrieved_at_utc DESC)", migration, StringComparison.Ordinal);
    }


    [Fact]
    public void ScoreMigration_ShouldEnforceTenantCandidateRelationshipAndScoreBounds()
    {
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.19", "5.9.19.sql");
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingScore.cs");

        Assert.Contains("tenant_id bigint NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY (tenant_id, candidate_id)", migration, StringComparison.Ordinal);
        Assert.Contains("CHECK (total BETWEEN 0 AND 100)", migration, StringComparison.Ordinal);
        Assert.Contains("CHECK (priority BETWEEN 0 AND 3)", migration, StringComparison.Ordinal);
        Assert.Contains("ix_crm_prosp_score_tenant_candidate_evaluated", migration, StringComparison.Ordinal);
        Assert.Contains("ix_crm_prosp_score_tenant_version_priority", migration, StringComparison.Ordinal);
        Assert.Contains("IStrictMultiTenantEntity", entity, StringComparison.Ordinal);
        Assert.Contains("ReviewGatesJson", entity, StringComparison.Ordinal);
    }


    [Fact]
    public void ScoreIntegrityMigration_ShouldRejectComponentTotalsThatDoNotMatch()
    {
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.20", "5.9.20.sql");

        Assert.Contains("ck_crm_prospecting_score_component_total", migration, StringComparison.Ordinal);
        Assert.Contains("total <> identity_points + procurement_points + product_fit_points", migration, StringComparison.Ordinal);
        Assert.Contains("RAISE EXCEPTION", migration, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT", migration, StringComparison.Ordinal);
        Assert.Contains("CHECK (", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceIdentityMigration_ShouldEnforceRetrySafeUniqueness()
    {
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.21", "5.9.21.sql");

        Assert.Contains("ux_crm_prosp_evidence_identity", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX", migration, StringComparison.Ordinal);
        Assert.Contains("tenant_id", migration, StringComparison.Ordinal);
        Assert.Contains("candidate_id", migration, StringComparison.Ordinal);
        Assert.Contains("provider_record_id SET NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("field_name", migration, StringComparison.Ordinal);
        Assert.Contains("source_url", migration, StringComparison.Ordinal);
        Assert.Contains("raw_payload_hash SET NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("source_url_hash", migration, StringComparison.Ordinal);
        Assert.Contains("digest(source_url", migration, StringComparison.Ordinal);
        Assert.Contains("sha256", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pgcrypto", migration, StringComparison.Ordinal);
        Assert.Contains("HAVING COUNT(*) > 1", migration, StringComparison.Ordinal);
        Assert.Contains("RAISE EXCEPTION", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceIdentityMigration_ShouldTargetTheCodeFirstTableAndDocumentFreshTenantGap()
    {
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingEvidence.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.21", "5.9.21.sql");
        var upgradeReadme = Read("backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "README.md");
        var operations = Read("docs", "kiger", "PROSPECTING-WORKER-OPERATIONS.md");

        Assert.Contains("TableName = \"Crm_Prospecting_Evidence\"", entity, StringComparison.Ordinal);
        Assert.Contains("Length = 2000", entity, StringComparison.Ordinal);
        Assert.Contains("Source_Url_Hash", entity, StringComparison.Ordinal);
        Assert.Contains("nameof(SourceUrlHash)", entity, StringComparison.Ordinal);
        Assert.Contains("to_regclass('crm_prospecting_evidence')", migration, StringComparison.Ordinal);
        Assert.Contains("fresh-tenant CodeFirst", operations, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CodeFirst", upgradeReadme, StringComparison.Ordinal);
        Assert.Contains("pgcrypto", upgradeReadme, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
