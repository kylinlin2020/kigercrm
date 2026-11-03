using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Guards the application transaction boundary and optimistic-lock write path used by Lead conversion.
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
    public void ConvertDomainWorkflow_ShouldPersistCustomerAndContactBeforeOptimisticallyUpdatingLead()
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
    }

    private static string RepositoryPath(params string[] segments) =>
        Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments)));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
