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

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
