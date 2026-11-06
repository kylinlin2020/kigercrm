using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingSuperSearchPlannerTests
{
    private static CrmProspectingSuperSearchInput Input(
        CrmProspectingSuperSearchDepth depth = CrmProspectingSuperSearchDepth.Deep,
        int? requestedRounds = null)
        => new(
            ["industrial pump", "generator"],
            ["Germany", "France"],
            ["Distributor", "Importer"],
            ["water treatment", "construction"],
            ["GLEIF", "serper"],
            ["consumer", "residential only"],
            ["example.com", ".blocked.example"],
            depth,
            requestedRounds);

    [Theory]
    [InlineData(CrmProspectingSuperSearchDepth.Balanced, 4)]
    [InlineData(CrmProspectingSuperSearchDepth.Deep, 8)]
    [InlineData(CrmProspectingSuperSearchDepth.Extreme, 16)]
    public void Create_ShouldApplyBoundedDepthProfiles(CrmProspectingSuperSearchDepth depth, int expectedRounds)
    {
        var plan = CrmProspectingSuperSearchPlanner.Create(Input(depth));

        Assert.Equal(expectedRounds, plan.MaximumRounds);
        Assert.Equal(expectedRounds, plan.PlannedRounds);
        Assert.Equal(expectedRounds * 2, plan.TotalQueryCells);
        Assert.All(plan.Rounds, round => Assert.Equal(2, round.QueryCells.Count));
    }

    [Fact]
    public void Create_ShouldBeDeterministicAndFingerprintDistinctRounds()
    {
        var first = CrmProspectingSuperSearchPlanner.Create(Input());
        var second = CrmProspectingSuperSearchPlanner.Create(Input());

        Assert.Equal(first.ScopeFingerprint, second.ScopeFingerprint);
        Assert.Equal(first.Rounds.Select(item => item.Fingerprint), second.Rounds.Select(item => item.Fingerprint));
        Assert.Equal(first.Rounds.Count, first.Rounds.Select(item => item.Fingerprint).Distinct().Count());
        Assert.Equal(first.Rounds.Sum(item => item.QueryCells.Count),
            first.Rounds.SelectMany(item => item.QueryCells).Select(item => item.Fingerprint).Distinct().Count());
    }

    [Fact]
    public void Create_ShouldCarryExclusionsIntoEveryRoundAndCell()
    {
        var plan = CrmProspectingSuperSearchPlanner.Create(Input());

        Assert.All(plan.Rounds, round =>
        {
            Assert.Contains("consumer", round.ExcludedTerms);
            Assert.Contains("residential only", round.ExcludedTerms);
            Assert.Contains("example.com", round.ExcludedDomains);
            Assert.Contains("blocked.example", round.ExcludedDomains);
            Assert.All(round.QueryCells, cell =>
            {
                Assert.Contains("industrial pump", cell.QueryText);
                Assert.Contains(round.Market, cell.QueryText);
                Assert.Contains(round.CustomerType, cell.QueryText);
            });
        });
    }

    [Fact]
    public void Create_ShouldNormalizeProviderCodesAndWhitespace()
    {
        var input = Input() with
        {
            Products = ["  industrial   pump ", "industrial pump"],
            ProviderCodes = [" GLEIF ", "gleif", "SERPER"]
        };

        var plan = CrmProspectingSuperSearchPlanner.Create(input);

        Assert.Equal(2, plan.ProviderCount);
        Assert.All(plan.Rounds.SelectMany(round => round.QueryCells), cell =>
            Assert.Contains(cell.ProviderCode, new[] { "gleif", "serper" }));
        Assert.Contains("industrial pump", plan.Rounds[0].QueryCells[0].QueryText);
    }

    [Theory]
    [InlineData(CrmProspectingSuperSearchDepth.Balanced, 5)]
    [InlineData(CrmProspectingSuperSearchDepth.Deep, 9)]
    [InlineData(CrmProspectingSuperSearchDepth.Extreme, 17)]
    public void Create_ShouldRejectRoundsBeyondDepth(CrmProspectingSuperSearchDepth depth, int requestedRounds)
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrmProspectingSuperSearchPlanner.Create(Input(depth, requestedRounds)));

    [Fact]
    public void Create_ShouldRejectMissingScopeOrOversizedProviderFanout()
    {
        Assert.Throws<ArgumentException>(() =>
            CrmProspectingSuperSearchPlanner.Create(Input() with { Products = [] }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrmProspectingSuperSearchPlanner.Create(Input() with
            {
                ProviderCodes = Enumerable.Range(0, 31).Select(index => $"provider_{index}").ToArray()
            }));
    }

    [Fact]
    public void Create_ShouldChangeScopeFingerprintWhenExclusionsChange()
    {
        var first = CrmProspectingSuperSearchPlanner.Create(Input());
        var second = CrmProspectingSuperSearchPlanner.Create(Input() with
        {
            ExcludedDomains = ["example.com", "another.example"]
        });

        Assert.NotEqual(first.ScopeFingerprint, second.ScopeFingerprint);
    }
}
