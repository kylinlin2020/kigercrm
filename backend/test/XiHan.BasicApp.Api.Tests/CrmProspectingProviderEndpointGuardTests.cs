using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level wiring guards ensure every official adapter uses the shared URI policy.</summary>
public sealed class CrmProspectingProviderEndpointGuardTests
{
    [Theory]
    [InlineData("CrmContractsFinderProspectingProvider.cs")]
    [InlineData("CrmFindTenderProspectingProvider.cs")]
    [InlineData("CrmTedProspectingProvider.cs")]
    [InlineData("CrmUsaSpendingProspectingProvider.cs")]
    [InlineData("CrmGleifIdentityResolver.cs")]
    public void FixedEndpointAdapters_ShouldUseSharedEndpointGuard(string fileName)
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Prospecting", fileName);

        Assert.Contains("CrmProspectingEndpointGuard.IsApprovedResponseUri", source, StringComparison.Ordinal);
        Assert.Contains("private static readonly Uri Endpoint = new(\"https://", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
