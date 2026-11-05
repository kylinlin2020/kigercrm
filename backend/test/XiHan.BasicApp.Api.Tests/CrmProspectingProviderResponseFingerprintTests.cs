using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingProviderResponseFingerprintTests
{
    [Fact]
    public void Create_ShouldReturnStableSha256ForEquivalentResponse()
    {
        var first = new[] { new { ExternalId = "A-1", Company = "Example Ltd" } };
        var second = new[] { new { ExternalId = "A-1", Company = "Example Ltd" } };

        var firstHash = CrmProspectingResponseFingerprint.Create(first);
        var secondHash = CrmProspectingResponseFingerprint.Create(second);

        Assert.Equal(firstHash, secondHash);
        Assert.Equal(64, firstHash.Length);
        Assert.All(firstHash, character => Assert.Contains(character, "0123456789ABCDEF"));
    }

    [Fact]
    public void Create_ShouldChangeWhenProviderResponseChanges()
    {
        var first = new[] { new { ExternalId = "A-1", Company = "Example Ltd" } };
        var changed = new[] { new { ExternalId = "A-2", Company = "Example Ltd" } };

        Assert.NotEqual(
            CrmProspectingResponseFingerprint.Create(first),
            CrmProspectingResponseFingerprint.Create(changed));
    }
}
