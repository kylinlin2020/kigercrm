using System.Reflection;
using System.Runtime.CompilerServices;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmPrimaryContactUniquenessTests
{
    [Fact]
    public void ContactEntity_ShouldDeclareTenantScopedUniquePrimarySlot()
    {
        var index = Assert.Single(typeof(SysCrmContact)
            .GetCustomAttributes<SugarIndexAttribute>(inherit: false)
            .Where(attribute => attribute.IsUnique));

        Assert.Equal(
            new[] { nameof(SysCrmContact.TenantId), nameof(SysCrmContact.PrimaryCustomerId) },
            index.IndexFields.Keys.ToArray());
        var slot = typeof(SysCrmContact).GetProperty(nameof(SysCrmContact.PrimaryCustomerId));
        Assert.NotNull(slot);
        Assert.True(Nullable.GetUnderlyingType(slot!.PropertyType) == typeof(long));
    }

    [Fact]
    public void ContactCreateAndUpdate_ShouldMaintainPrimarySlot()
    {
        var source = File.ReadAllText(RepositoryPath(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain",
            "DomainServices", "Implementations", "CrmContactDomainService.cs"));

        Assert.Equal(2, source.Split(
            "entity.PrimaryCustomerId = command.IsPrimary ? command.CustomerId : null;",
            StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Migration_ShouldBackfillAndRejectExistingDuplicatePrimaryContacts()
    {
        var source = File.ReadAllText(RepositoryPath(
            "backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts",
            "5.9.3", "5.9.3.sql"));

        Assert.Contains("ADD COLUMN IF NOT EXISTS primary_customer_id bigint", source, StringComparison.Ordinal);
        Assert.Contains("WHEN is_primary THEN customer_id", source, StringComparison.Ordinal);
        Assert.Contains("GROUP BY tenant_id, customer_id", source, StringComparison.Ordinal);
        Assert.Contains("HAVING COUNT(*) > 1", source, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_crm_contact_teid_prcuid", source, StringComparison.Ordinal);
    }

    private static string RepositoryPath(params string[] segments) =>
        Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments)));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
