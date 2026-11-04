using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingScorecardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompleteRecentProcurementEvidence_ShouldReceivePriorityA()
    {
        var result = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(true, true, true, true, Now.AddDays(-10), true, true), Now);

        Assert.Equal(100, result.Total);
        Assert.Equal(CrmProspectingPriority.A, result.Priority);
        Assert.Empty(result.ReviewGates);
        Assert.Equal(CrmProspectingScorecard.Version, result.ScorecardVersion);
    }

    [Fact]
    public void MissingEmail_ShouldNotBlockStrongBuyer()
    {
        var result = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(true, true, true, true, Now.AddDays(-20), true, false), Now);

        Assert.Equal(95, result.Total);
        Assert.Equal(CrmProspectingPriority.A, result.Priority);
    }

    [Fact]
    public void SearchSnippetWithoutPrimaryEvidence_ShouldBeHeld()
    {
        var result = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(true, false, true, true, Now.AddDays(-5), false, true), Now);

        Assert.Equal(CrmProspectingPriority.Hold, result.Priority);
        Assert.Contains("missing_traceable_primary_evidence", result.ReviewGates);
        Assert.Contains("missing_direct_procurement_signal", result.ReviewGates);
    }

    [Fact]
    public void DuplicateOrUnsafeSource_ShouldAlwaysBeHeld()
    {
        var duplicate = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(true, true, true, true, Now, true, true, IsKnownDuplicate: true), Now);
        var unsafeSource = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(true, true, true, true, Now, true, true, IsRestrictedOrUnsafeSource: true), Now);

        Assert.Equal(CrmProspectingPriority.Hold, duplicate.Priority);
        Assert.Equal(CrmProspectingPriority.Hold, unsafeSource.Priority);
    }

    [Theory]
    [InlineData(15, 10)]
    [InlineData(60, 8)]
    [InlineData(120, 6)]
    [InlineData(300, 3)]
    [InlineData(500, 0)]
    public void RecencyPoints_ShouldFollowPublishedDateBands(int ageDays, int expected)
    {
        var result = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(false, false, false, false, Now.AddDays(-ageDays), false, false), Now);

        Assert.Equal(expected, result.RecencyPoints);
    }

    [Fact]
    public void FuturePublicationDate_ShouldReceiveNoRecencyPoints()
    {
        var result = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(false, false, false, false, Now.AddDays(1), false, false), Now);

        Assert.Equal(0, result.RecencyPoints);
    }

    [Fact]
    public void NonUtcEvaluationTime_ShouldBeRejected()
    {
        Assert.Throws<ArgumentException>(() => CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(false, false, false, false, null, false, false),
            new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(1))));
    }
}
