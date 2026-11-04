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
    public void Create_ShouldPersistDeterministicReferencesToTheExactEvidenceSnapshot()
    {
        var score = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(false, false, false, false, null, true, false), Now);
        var evidence = new[]
        {
            new XiHan.BasicApp.Crm.Domain.Entities.SysCrmProspectingEvidence
            {
                ProviderCode = "registry",
                ProviderRecordId = "record-2",
                FieldName = "company_name",
                SourceUrlHash = new string('A', 64),
                RawPayloadHash = new string('B', 64),
                EvidenceType = "registry",
                VerificationStatus = "unverified"
            },
            new XiHan.BasicApp.Crm.Domain.Entities.SysCrmProspectingEvidence
            {
                ProviderCode = "registry",
                ProviderRecordId = "record-1",
                FieldName = "website",
                SourceUrlHash = new string('C', 64),
                RawPayloadHash = new string('D', 64),
                EvidenceType = "direct",
                VerificationStatus = "unverified"
            }
        };

        var first = CrmProspectingScoreSnapshotFactory.Create(123, score, evidence);
        var second = CrmProspectingScoreSnapshotFactory.Create(123, score, evidence.Reverse().ToArray());

        Assert.Equal(first.EvidenceReferencesJson, second.EvidenceReferencesJson);
        Assert.Contains("record-1", first.EvidenceReferencesJson, StringComparison.Ordinal);
        Assert.Contains(new string('C', 64), first.EvidenceReferencesJson, StringComparison.Ordinal);
        Assert.Contains("company_name", first.EvidenceReferencesJson, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", first.EvidenceReferencesJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ShouldRejectExcessiveEvidenceReferenceCount()
    {
        var score = CrmProspectingScorecard.Evaluate(
            new CrmProspectingScoreInput(false, false, false, false, null, false, false), Now);
        var evidence = Enumerable.Range(0, 101)
            .Select(index => new XiHan.BasicApp.Crm.Domain.Entities.SysCrmProspectingEvidence
            {
                ProviderCode = "provider",
                ProviderRecordId = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FieldName = "company_name",
                SourceUrlHash = new string('A', 64),
                RawPayloadHash = new string('B', 64)
            })
            .ToArray();

        Assert.Throws<ArgumentException>(() => CrmProspectingScoreSnapshotFactory.Create(1, score, evidence));
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
