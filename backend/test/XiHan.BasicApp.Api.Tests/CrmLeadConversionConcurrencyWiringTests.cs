using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Guards the application transaction boundary and tenant-scoped database uniqueness used by Lead conversion.
/// This is source-wiring coverage, not a substitute for a two-connection database concurrency test.
/// </summary>
public sealed class CrmLeadConversionConcurrencyWiringTests
{
    [Fact]
    public void ConvertEndpoint_ShouldRunInsideTransactionalUnitOfWork()
    {
        var source = File.ReadAllText(RepositoryPath(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "AppServices", "CrmLeadAppService.cs"));

        var convert = source.IndexOf("public async Task<CrmLeadDetailDto> ConvertAsync", StringComparison.Ordinal);
        Assert.True(convert >= 0, "Lead conversion endpoint was not found.");

        var preceding = source[..convert];
        var unitOfWork = preceding.LastIndexOf("[UnitOfWork(true)]", StringComparison.Ordinal);
        var permission = preceding.LastIndexOf("[PermissionAuthorize(CrmPermissionCodes.Execute)]", StringComparison.Ordinal);

        Assert.True(unitOfWork >= 0 && permission > unitOfWork,
            "ConvertAsync must retain its transactional UnitOfWork attribute and Execute permission.");
    }

    [Fact]
    public void ConvertDomainWorkflow_ShouldPersistCustomerAndContactBeforeUpdatingLead()
    {
        var source = File.ReadAllText(RepositoryPath(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain",
            "DomainServices", "Implementations", "CrmLeadDomainService.cs"));

        var customerInsert = source.IndexOf("customers.AddAsync", StringComparison.Ordinal);
        var contactInsert = source.IndexOf("contacts.AddAsync", StringComparison.Ordinal);
        var leadUpdate = source.IndexOf("repository.UpdateAsync(lead", StringComparison.Ordinal);

        Assert.True(customerInsert >= 0);
        Assert.True(contactInsert > customerInsert);
        Assert.True(leadUpdate > contactInsert);
        Assert.Contains("if (lead.Status == CrmLeadStatus.Converted)", source, StringComparison.Ordinal);
        Assert.Contains("return lead;", source, StringComparison.Ordinal);
        Assert.Contains("ConvertedLeadId = lead.BasicId", source, StringComparison.Ordinal);

        var customerEntity = File.ReadAllText(RepositoryPath(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "SysCrmCustomer.cs"));
        Assert.Contains("nameof(ConvertedLeadId)", customerEntity, StringComparison.Ordinal);
        Assert.Contains("UX_{table}_TeId_ConvLead", customerEntity, StringComparison.Ordinal);

        var migration = File.ReadAllText(RepositoryPath(
            "backend", "src", "main", "XiHan.BasicApp.WebHost", "UpdateScripts", "5.9.28", "5.9.28.sql"));
        Assert.Contains("ADD COLUMN IF NOT EXISTS converted_lead_id", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE UNIQUE INDEX", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(tenant_id, converted_lead_id)", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ux_crm_customer_teid_convlead", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UX_{table}_TeId_ConvLead", customerEntity, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertedLeadRetry_ShouldResolveExistingCustomerAndContactWithoutCreatingDuplicates()
    {
        var source = File.ReadAllText(RepositoryPath(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain",
            "DomainServices", "Implementations", "CrmLeadDomainService.cs"));

        var convertedBranch = source.IndexOf(
            "if (lead.Status == CrmLeadStatus.Converted)", StringComparison.Ordinal);
        var disqualifiedBranch = source.IndexOf(
            "if (lead.Status == CrmLeadStatus.Disqualified)", convertedBranch, StringComparison.Ordinal);
        Assert.True(convertedBranch >= 0 && disqualifiedBranch > convertedBranch,
            "The already-converted retry branch must precede all new conversion writes.");

        var retryPath = source[convertedBranch..disqualifiedBranch];
        Assert.Contains("lead.ConvertedCustomerId is not > 0", retryPath, StringComparison.Ordinal);
        Assert.Contains("customers.GetByIdAsync(lead.ConvertedCustomerId.Value", retryPath, StringComparison.Ordinal);
        Assert.Contains("lead.ConvertedContactId is > 0", retryPath, StringComparison.Ordinal);
        Assert.Contains("contacts.GetByIdAsync(lead.ConvertedContactId.Value", retryPath, StringComparison.Ordinal);
        Assert.Contains("return lead;", retryPath, StringComparison.Ordinal);
        Assert.DoesNotContain("customers.AddAsync", retryPath, StringComparison.Ordinal);
        Assert.DoesNotContain("contacts.AddAsync", retryPath, StringComparison.Ordinal);
    }

    private static string RepositoryPath(params string[] segments) =>
        Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments)));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
