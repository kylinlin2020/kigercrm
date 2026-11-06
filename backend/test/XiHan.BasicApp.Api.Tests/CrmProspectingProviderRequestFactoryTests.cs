using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingProviderRequestFactoryTests
{
    [Fact]
    public void Create_UsesOnlyImmutableRunSnapshot()
    {
        var run = new SysCrmProspectingRun
        {
            BasicId = 42,
            SnapshotSearchObjective = "Find industrial generator distributors",
            SnapshotProductScope = "Diesel generators",
            SnapshotTargetMarkets = "Brazil;Chile",
            SnapshotIdealCustomerProfile = "Importers and wholesalers",
            SnapshotProviderPlanJson = "[\"provider-a\"]",
            SnapshotExcludedDomainsJson = "[\"example.com\"]"
        };

        var request = CrmProspectingProviderRequestFactory.Create(run, ["Example.com", "blocked.test"]);

        Assert.Equal(42, request.RunId);
        Assert.Equal("Find industrial generator distributors", request.SearchObjective);
        Assert.Equal("Diesel generators", request.ProductScope);
        Assert.Equal("Brazil;Chile", request.TargetMarkets);
        Assert.Equal("Importers and wholesalers", request.IdealCustomerProfile);
        Assert.Equal("{}", request.ProviderOptionsJson);
        Assert.Equal(["blocked.test", "example.com"], request.ExcludedDomains);
    }

    [Fact]
    public void Create_NormalizesAndDeduplicatesExcludedDomainsDeterministically()
    {
        var run = new SysCrmProspectingRun
        {
            BasicId = 7,
            SnapshotSearchObjective = "Find pump distributors"
        };

        var first = CrmProspectingProviderRequestFactory.Create(
            run, [" B.example ", "a.example", "b.example", "", "  "]);
        var second = CrmProspectingProviderRequestFactory.Create(
            run, ["a.example", "b.example"]);

        Assert.Equal(first.ExcludedDomains, second.ExcludedDomains);
        Assert.Equal(["a.example", "b.example"], first.ExcludedDomains);
    }

    [Fact]
    public void Create_RejectsMissingPersistedRunIdentityOrSearchObjective()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrmProspectingProviderRequestFactory.Create(
                new SysCrmProspectingRun { SnapshotSearchObjective = "valid" }, []));
        Assert.Throws<ArgumentException>(() =>
            CrmProspectingProviderRequestFactory.Create(
                new SysCrmProspectingRun { BasicId = 1 }, []));
    }
}
