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
    public void ProspectingRun_ShouldBePermanentCreationOnlyIdempotencyLedger()
    {
        var entity = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmProspectingRun.cs");
        Assert.Contains("BasicAppCreationEntity", entity, StringComparison.Ordinal);
        Assert.Contains("IStrictMultiTenantEntity", entity, StringComparison.Ordinal);
        Assert.Contains("UX_{table}_Te_Rq", entity, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDeleted", entity, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunStatus", entity, StringComparison.Ordinal);
    }

    [Fact]
    public void ProspectingRepositories_ShouldUseSaasRepositoryBase()
    {
        foreach (var name in new[] { "Campaign", "Strategy", "Run" })
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

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
