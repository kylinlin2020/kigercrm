using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmCommandAuthorizationTests
{
    private static readonly string[] AppServiceFiles =
    [
        "CrmActivityAppService.cs",
        "CrmContactAppService.cs",
        "CrmCustomerAppService.cs",
        "CrmLeadAppService.cs",
        "CrmOpportunityAppService.cs",
        "CrmQuoteAppService.cs",
        "CrmSalesOrderAppService.cs",
        "CrmShipmentAppService.cs",
        "CrmTaskAppService.cs"
    ];

    [Fact]
    public void EveryPublicCommand_ShouldDeclareTransactionAndPermission()
    {
        foreach (var fileName in AppServiceFiles)
        {
            var source = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "backend", "src", "modules", "XiHan.BasicApp.Crm",
                "Application", "AppServices", fileName));
            var lines = source.Split('\n');
            var commandCount = 0;

            for (var index = 0; index < lines.Length; index++)
            {
                var signature = lines[index].TrimStart();
                if (!signature.StartsWith("public async Task", StringComparison.Ordinal))
                {
                    continue;
                }

                commandCount++;
                var attributes = string.Join("\n", lines.Skip(Math.Max(0, index - 4)).Take(Math.Min(4, index)));
                Assert.Contains("[UnitOfWork(true)]", attributes, StringComparison.Ordinal);
                Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.", attributes, StringComparison.Ordinal);
            }

            Assert.True(commandCount > 0, $"{fileName} should expose at least one public command.");
        }
    }

    [Fact]
    public void CrmPermissionCatalogAndPageButtons_ShouldCoverAllCommandOperations()
    {
        var root = RepositoryRoot();
        var codes = File.ReadAllText(Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Permissions", "CrmPermissionCodes.cs"));
        var catalog = File.ReadAllText(Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Seeders", "CrmPermissionCatalogSeeder.cs"));
        var pages = File.ReadAllText(Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Pages", "PageRegistry.cs"));

        foreach (var operation in new[] { "Read", "Create", "Update", "Delete", "Execute" })
        {
            Assert.Contains($"public const string {operation} = \"crm:", codes, StringComparison.Ordinal);
            Assert.Contains($"OperationSeeds.{operation}", catalog, StringComparison.Ordinal);
        }

        Assert.Contains("CrmPermissionCodes.Read", pages, StringComparison.Ordinal);
        Assert.Contains("CrmPermissionCodes.Create", pages, StringComparison.Ordinal);
        Assert.Contains("CrmPermissionCodes.Update", pages, StringComparison.Ordinal);
        Assert.Contains("CrmPermissionCodes.Delete", pages, StringComparison.Ordinal);
        Assert.Contains("CrmPermissionCodes.Execute", pages, StringComparison.Ordinal);
    }


    [Fact]
    public void CrmExportProviders_ShouldRequireExecutePermissionRatherThanRead()
    {
        var root = RepositoryRoot();
        var providerNames = new[]
        {
            "CrmLeadExportProvider.cs",
            "CrmCustomerExportProvider.cs",
            "CrmContactExportProvider.cs",
            "CrmOpportunityExportProvider.cs",
            "CrmQuoteExportProvider.cs",
            "CrmSalesOrderExportProvider.cs",
            "CrmShipmentExportProvider.cs"
        };

        foreach (var providerName in providerNames)
        {
            var source = File.ReadAllText(Path.Combine(
                root, "backend", "src", "modules", "XiHan.BasicApp.Crm",
                "Application", "Exporting", providerName));

            Assert.Contains(
                "public override string RequiredPermission => CrmPermissionCodes.Execute;",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "RequiredPermission => CrmPermissionCodes.Read",
                source,
                StringComparison.Ordinal);
        }

        var catalog = File.ReadAllText(Path.Combine(
            root, "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Seeders", "CrmPermissionCatalogSeeder.cs"));
        Assert.Contains("OperationSeeds.Execute", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void CrmExportProviders_ShouldHavePageExportButtonsBoundToExecutePermission()
    {
        var root = RepositoryRoot();
        var pages = File.ReadAllText(Path.Combine(
            root, "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "Pages", "PageRegistry.cs"));
        var providerPages = new[]
        {
            "crm_leads",
            "crm_customers",
            "crm_contacts",
            "crm_opportunities",
            "crm_quotes",
            "crm_sales_orders",
            "crm_shipments"
        };

        foreach (var pageCode in providerPages)
        {
            Assert.Contains(
                $"new(\"{pageCode}.export\", \"Export\", \"{pageCode}\", CrmPermissionCodes.Execute",
                pages,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LeadCsvPreview_ShouldRequireExecuteAndRemainPreviewOnly()
    {
        var root = RepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            root, "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "AppServices", "CrmLeadImportPreviewAppService.cs"));
        var contract = File.ReadAllText(Path.Combine(
            root, "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "Contracts", "ICrmLeadImportPreviewAppService.cs"));

        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", service, StringComparison.Ordinal);
        Assert.Contains("CrmCsvImportPreviewService.Preview(", service, StringComparison.Ordinal);
        Assert.Contains("RequiredFields = [\"CompanyName\"]", service, StringComparison.Ordinal);
        Assert.DoesNotContain("\"DuplicateEmailNormalized\"", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ICrmLeadDomainService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ICrmLeadRepository", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsync(", service, StringComparison.Ordinal);
        Assert.Contains("PreviewAsync(", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerAndContactCsvPreviews_ShouldUseExecuteAndResourceSpecificAllowLists()
    {
        var root = RepositoryRoot();
        var crm = Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm");
        var customer = File.ReadAllText(Path.Combine(crm, "Application", "AppServices", "CrmCustomerImportPreviewAppService.cs"));
        var contact = File.ReadAllText(Path.Combine(crm, "Application", "AppServices", "CrmContactImportPreviewAppService.cs"));

        foreach (var service in new[] { customer, contact })
        {
            Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", service, StringComparison.Ordinal);
            Assert.Contains("CrmCsvImportPreviewService.Preview(", service, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateAsync(", service, StringComparison.Ordinal);
            Assert.DoesNotContain("UpdateAsync(", service, StringComparison.Ordinal);
        }

        Assert.Contains("RequiredFields = [\"Name\"]", customer, StringComparison.Ordinal);
        Assert.Contains("RequiredFields = [\"CustomerId\", \"FirstName\", \"LastName\"]", contact, StringComparison.Ordinal);
        Assert.DoesNotContain("\"OwnerUserId\"", customer, StringComparison.Ordinal);
        Assert.DoesNotContain("\"OwnerUserId\"", contact, StringComparison.Ordinal);
    }

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));

    [Fact]
    public void CsvPreviewEndpoints_ShouldShareGenericRequestContract()
    {
        var root = RepositoryRoot();
        var crm = Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm");
        var contracts = new[]
        {
            Path.Combine(crm, "Application", "Contracts", "ICrmLeadImportPreviewAppService.cs"),
            Path.Combine(crm, "Application", "Contracts", "ICrmCustomerImportPreviewAppService.cs"),
            Path.Combine(crm, "Application", "Contracts", "ICrmContactImportPreviewAppService.cs")
        };
        var services = new[]
        {
            Path.Combine(crm, "Application", "AppServices", "CrmLeadImportPreviewAppService.cs"),
            Path.Combine(crm, "Application", "AppServices", "CrmCustomerImportPreviewAppService.cs"),
            Path.Combine(crm, "Application", "AppServices", "CrmContactImportPreviewAppService.cs")
        };

        foreach (var path in contracts.Concat(services))
        {
            var source = File.ReadAllText(path);
            Assert.Contains("CrmCsvImportPreviewRequestDto", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CrmLeadImportPreviewRequestDto", source, StringComparison.Ordinal);
        }

        var sharedDto = File.ReadAllText(Path.Combine(
            crm, "Application", "Importing", "CrmCsvImportPreviewRequestDto.cs"));
        Assert.Contains("byte[] FileBytes", sharedDto, StringComparison.Ordinal);
        Assert.Contains("Dictionary<string, string>? FieldMapping", sharedDto, StringComparison.Ordinal);
    }
}
