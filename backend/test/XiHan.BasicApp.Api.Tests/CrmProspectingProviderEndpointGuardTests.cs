using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Source-level regression guards for fixed provider endpoints. These guards do not replace
/// live socket/DNS tests, but prevent adapters from accepting alternate ports or URI user-info.
/// </summary>
public sealed class CrmProspectingProviderEndpointGuardTests
{
    [Theory]
    [InlineData("CrmContractsFinderProspectingProvider.cs")]
    [InlineData("CrmFindTenderProspectingProvider.cs")]
    [InlineData("CrmTedProspectingProvider.cs")]
    [InlineData("CrmUsaSpendingProspectingProvider.cs")]
    [InlineData("CrmGleifIdentityResolver.cs")]
    public void FixedEndpointAdapters_ShouldRejectNonDefaultPortsAndUriUserInfo(string fileName)
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Prospecting", fileName);

        Assert.Contains("!finalUri.IsDefaultPort", source, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrEmpty(finalUri.UserInfo)", source, StringComparison.Ordinal);
        Assert.Contains("finalUri.Scheme, Uri.UriSchemeHttps", source, StringComparison.Ordinal);
        Assert.Contains("finalUri.Host, Endpoint.Host", source, StringComparison.Ordinal);
        Assert.Contains("finalUri.AbsolutePath, Endpoint.AbsolutePath", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
