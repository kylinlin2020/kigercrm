using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmLeadDuplicateLookupWiringTests
{
    [Fact]
    public void DuplicateQuery_ShouldUseNormalizedIndexedIdentityColumns()
    {
        var source = File.ReadAllText(RepositoryPath(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "QueryServices", "CrmLeadDuplicateQueryService.cs"));

        Assert.Contains("lead.DuplicateEmailNormalized", source, StringComparison.Ordinal);
        Assert.Contains("lead.PhoneDigits", source, StringComparison.Ordinal);
        Assert.Contains("lead.WebsiteHost", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lead.Phone, phone", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lead.Website, website", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LeadCreateAndUpdate_ShouldMaintainNormalizedIdentityColumns()
    {
        var source = File.ReadAllText(RepositoryPath(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain",
            "DomainServices", "Implementations", "CrmLeadDomainService.cs"));

        Assert.Contains("entity.DuplicateEmailNormalized = CrmLeadDuplicateMatcher.NormalizeEmail(entity.Email)", source, StringComparison.Ordinal);
        Assert.Contains("entity.PhoneDigits = CrmLeadDuplicateMatcher.NormalizePhone(entity.Phone)", source, StringComparison.Ordinal);
        Assert.Contains("entity.WebsiteHost = CrmLeadDuplicateMatcher.NormalizeDomain(entity.Website)", source, StringComparison.Ordinal);
        Assert.Equal(1, source.Split("private static void Apply(", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Migration_ShouldBackfillKeysAndCreateOnlyNonUniqueIndexes()
    {
        var source = File.ReadAllText(RepositoryPath(
            "backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts",
            "5.9.2", "5.9.2.sql"));

        Assert.Contains("ADD COLUMN IF NOT EXISTS duplicate_email_normalized", source, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS phone_digits", source, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS website_host", source, StringComparison.Ordinal);
        Assert.Contains("UPDATE crm_lead", source, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS ix_crm_lead_tenant_phone_digits", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE UNIQUE INDEX", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryPath(params string[] segments) =>
        Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments)));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
