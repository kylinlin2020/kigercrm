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


    [Fact]
    public void OfficialProviderRegistrations_ShouldUsePinnedPublicIpHandlers()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            RepositoryRoot(), "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Extensions", "ServiceCollectionExtensions.cs")));

        Assert.Contains("CrmProspectingHttpHandlerFactory.Create(\"api.usaspending.gov\")", source, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingHttpHandlerFactory.Create(\"www.find-tender.service.gov.uk\")", source, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingHttpHandlerFactory.Create(\"www.contractsfinder.service.gov.uk\")", source, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingHttpHandlerFactory.Create(\"api.ted.europa.eu\")", source, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingHttpHandlerFactory.Create(\"api.gleif.org\")", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
