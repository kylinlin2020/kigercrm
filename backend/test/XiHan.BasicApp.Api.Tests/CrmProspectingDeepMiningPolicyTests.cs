using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingDeepMiningPolicyTests
{
    [Theory]
    [InlineData(CrmProspectingSuperSearchDepth.Balanced, 1, 3, 3, 2)]
    [InlineData(CrmProspectingSuperSearchDepth.Deep, 2, 10, 7, 5)]
    [InlineData(CrmProspectingSuperSearchDepth.Extreme, 3, 20, 15, 10)]
    public void GetLimits_ShouldReturnBoundedProfiles(
        CrmProspectingSuperSearchDepth depth,
        int maxDepth,
        int maxRoots,
        int maxQueries,
        int maxProbes)
    {
        var limits = CrmProspectingDeepMiningPolicy.GetLimits(depth);

        Assert.Equal(maxDepth, limits.MaximumDepth);
        Assert.Equal(maxRoots, limits.MaximumRootCandidates);
        Assert.Equal(maxQueries, limits.MaximumQueries);
        Assert.Equal(maxProbes, limits.MaximumWebsiteProbes);
    }

    [Fact]
    public void Evaluate_ShouldAllowWorkOnlyInsideEveryBudget()
    {
        var decision = CrmProspectingDeepMiningPolicy.Evaluate(
            CrmProspectingSuperSearchDepth.Deep,
            rootCandidateCount: 4,
            taskDepth: 1,
            queriesUsed: 3,
            websiteProbesUsed: 2);

        Assert.True(decision.Allowed);
        Assert.Equal("within_limits", decision.ReasonCode);
    }

    [Theory]
    [InlineData(true, false, false, false, "cancelled")]
    [InlineData(false, true, false, false, "deadline_reached")]
    [InlineData(false, false, true, false, "cost_limit_reached")]
    [InlineData(false, false, false, true, "duplicate_saturation")]
    public void Evaluate_ShouldStopForTerminalMissionConditions(
        bool cancelled,
        bool deadlineReached,
        bool costLimitReached,
        bool duplicateSaturationReached,
        string expectedReason)
    {
        var decision = CrmProspectingDeepMiningPolicy.Evaluate(
            CrmProspectingSuperSearchDepth.Deep,
            1, 0, 0, 0,
            cancelled, deadlineReached, costLimitReached, duplicateSaturationReached);

        Assert.False(decision.Allowed);
        Assert.Equal(expectedReason, decision.ReasonCode);
    }

    [Theory]
    [InlineData(11, 0, 0, 0, "root_candidate_limit")]
    [InlineData(1, 2, 0, 0, "maximum_depth")]
    [InlineData(1, 0, 7, 0, "query_limit")]
    [InlineData(1, 0, 0, 5, "website_probe_limit")]
    [InlineData(0, 0, 0, 0, "no_root_candidates")]
    public void Evaluate_ShouldEnforceHardResourceLimits(
        int rootCount, int taskDepth, int queries, int probes, string expectedReason)
    {
        var decision = CrmProspectingDeepMiningPolicy.Evaluate(
            CrmProspectingSuperSearchDepth.Deep,
            rootCount, taskDepth, queries, probes);

        Assert.False(decision.Allowed);
        Assert.Equal(expectedReason, decision.ReasonCode);
    }

    [Fact]
    public void CreateTaskId_ShouldBeStableAndScopedToMissionRootAndCandidate()
    {
        var first = CrmProspectingDeepMiningPolicy.CreateTaskId("mission-1", "root-1", "candidate-1");

        Assert.Equal(first, CrmProspectingDeepMiningPolicy.CreateTaskId("mission-1", "root-1", "candidate-1"));
        Assert.NotEqual(first, CrmProspectingDeepMiningPolicy.CreateTaskId("mission-2", "root-1", "candidate-1"));
        Assert.NotEqual(first, CrmProspectingDeepMiningPolicy.CreateTaskId("mission-1", "root-2", "candidate-1"));
        Assert.NotEqual(first, CrmProspectingDeepMiningPolicy.CreateTaskId("mission-1", "root-1", "candidate-2"));
        Assert.StartsWith("pdmt_", first);
    }

    [Fact]
    public void BuildRelationshipQuery_ShouldIncludeScopeAndExplicitExclusions()
    {
        var query = CrmProspectingDeepMiningPolicy.BuildRelationshipQuery(
            "  Example   Pumps ",
            "industrial water treatment",
            "Germany",
            ["consumer", "residential"]);

        Assert.Contains("Example Pumps", query);
        Assert.Contains("industrial water treatment", query);
        Assert.Contains("Germany", query);
        Assert.Contains("subsidiary", query);
        Assert.Contains("-consumer", query);
        Assert.Contains("-residential", query);
    }

    [Fact]
    public void BuildRelationshipQuery_ShouldRejectControlCharacters()
        => Assert.Throws<ArgumentException>(() =>
            CrmProspectingDeepMiningPolicy.BuildRelationshipQuery("Example\nPumps", null, null));
}
