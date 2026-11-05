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

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
