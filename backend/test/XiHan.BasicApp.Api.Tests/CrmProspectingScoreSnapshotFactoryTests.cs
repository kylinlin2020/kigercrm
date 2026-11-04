using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingScoreSnapshotFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ShouldPreserveVersionComponentsAndReviewGates()
    {
        var score = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(true, false, true, true, Now.AddDays(-10), true, true), Now);

        var snapshot = CrmProspectingScoreSnapshotFactory.Create(123, score);

        Assert.Equal(123, snapshot.CandidateId);
        Assert.Equal(CrmProspectingScorecard.Version, snapshot.ScorecardVersion);
        Assert.Equal(score.Total, snapshot.Total);
        Assert.Equal(score.IdentityPoints, snapshot.IdentityPoints);
        Assert.Contains("missing_direct_procurement_signal", snapshot.ReviewGatesJson);
        Assert.Equal(Now, snapshot.EvaluatedAtUtc);
    }

    [Fact]
    public void Create_ShouldRejectComponentTotalMismatch()
    {
        var score = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(true, true, true, true, Now, true, true), Now) with { ContactPoints = 0 };

        Assert.Throws<ArgumentException>(() => CrmProspectingScoreSnapshotFactory.Create(1, score));
    }

    [Fact]
    public void Create_ShouldRejectUndefinedPriorityAndBlankReviewGates()
    {
        var score = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(false, false, false, false, null, false, false), Now);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrmProspectingScoreSnapshotFactory.Create(1, score with { Priority = (CrmProspectingPriority)99 }));

        Assert.Throws<ArgumentException>(() =>
            CrmProspectingScoreSnapshotFactory.Create(1, score with { ReviewGates = [" ", "missing_evidence"] }));
    }

    [Fact]
    public void Create_ShouldRejectInvalidCandidateIdAndOverlongScorecardVersion()
    {
        var score = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(false, false, false, false, null, false, false), Now);

        Assert.Throws<ArgumentOutOfRangeException>(() => CrmProspectingScoreSnapshotFactory.Create(0, score));
        Assert.Throws<ArgumentException>(() =>
            CrmProspectingScoreSnapshotFactory.Create(1, score with { ScorecardVersion = new string('v', 81) }));
    }

    [Fact]
    public void Create_ShouldRejectExcessiveReviewGateCountAndLength()
    {
        var score = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(false, false, false, false, null, false, false), Now);

        Assert.Throws<ArgumentException>(() =>
            CrmProspectingScoreSnapshotFactory.Create(1, score with
            {
                ReviewGates = Enumerable.Range(0, 51).Select(index => $"gate_{index}").ToArray()
            }));

        Assert.Throws<ArgumentException>(() =>
            CrmProspectingScoreSnapshotFactory.Create(1, score with
            {
                ReviewGates = [new string('g', 121)]
            }));
    }

    [Fact]
    public void Create_ShouldRejectNonUtcTimestamp()
    {
        var score = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(false, false, false, false, null, false, false), Now)
            with { EvaluatedAtUtc = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(1)) };

        Assert.Throws<ArgumentException>(() => CrmProspectingScoreSnapshotFactory.Create(1, score));
    }
}
