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
            SnapshotSearchObjective = "Find industrial generator distributors",
            SnapshotProductScope = "Diesel generators",
            SnapshotTargetMarkets = "Brazil;Chile",
            SnapshotIdealCustomerProfile = "Importers and wholesalers",
            SnapshotProviderPlanJson = "[\"provider-a\"]",
            SnapshotExcludedDomainsJson = "[\"example.com\"]"
        };
        run.SetBasicId(42);

        var request = CrmProspectingProviderRequestFactory.Create(run, ["Example.com", "blocked.test"]);

        Assert.Equal(42, request.RunId);
        Assert.Equal("Find industrial generator distributors", request.SearchObjective);
        Assert.Equal("Diesel generators", request.ProductScope);
        Assert.Equal("Brazil;Chile", request.TargetMarkets);
        Assert.Equal("Importers and wholesalers", request.IdealCustomerProfile);
        Assert.Equal("{}", request.ProviderOptionsJson);
        Assert.Equal(new[] { "blocked.test", "example.com" }, request.ExcludedDomains);
    }

    [Fact]
    public void Create_NormalizesAndDeduplicatesExcludedDomainsDeterministically()
    {
        var run = new SysCrmProspectingRun
        {
            SnapshotSearchObjective = "Find pump distributors"
        };
        run.SetBasicId(7);

        var first = CrmProspectingProviderRequestFactory.Create(
            run, [" B.example ", "a.example", "b.example", "", "  "]);
        var second = CrmProspectingProviderRequestFactory.Create(
            run, ["a.example", "b.example"]);

        Assert.Equal(first.ExcludedDomains, second.ExcludedDomains);
        Assert.Equal(new[] { "a.example", "b.example" }, first.ExcludedDomains);
    }

    [Fact]
    public void Create_RejectsMissingPersistedRunIdentityOrSearchObjective()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrmProspectingProviderRequestFactory.Create(
                new SysCrmProspectingRun { SnapshotSearchObjective = "valid" }, []));
        var runWithId = new SysCrmProspectingRun();
        runWithId.SetBasicId(1);
        Assert.Throws<ArgumentException>(() =>
            CrmProspectingProviderRequestFactory.Create(runWithId, []));
    }
}
