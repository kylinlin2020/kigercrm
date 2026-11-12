// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.Crm.Application.QueryServices;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingRunProgressBuilderTests
{
    [Fact]
    public void Build_WithNoSteps_ReportsZeroProgressInsteadOfFabricatingCompletion()
    {
        var progress = CrmProspectingRunProgressBuilder.Build([]);

        Assert.Equal(0, progress.TotalSteps);
        Assert.Equal(0, progress.CompletedSteps);
        Assert.Equal(0, progress.Percent);
    }

    [Fact]
    public void Build_CountsEachKnownStateInItsOwnBucket()
    {
        var progress = CrmProspectingRunProgressBuilder.Build(Rows(
            "Queued", "Started", "Checkpointed", "Projecting",
            "Succeeded", "Failed", "NeedsReview", "Skipped"));

        Assert.Equal(8, progress.TotalSteps);
        Assert.Equal(1, progress.QueuedSteps);
        Assert.Equal(3, progress.RunningSteps);
        Assert.Equal(1, progress.SucceededSteps);
        Assert.Equal(1, progress.FailedSteps);
        Assert.Equal(1, progress.NeedsReviewSteps);
        Assert.Equal(1, progress.SkippedSteps);
        // Succeeded + Failed + Skipped + NeedsReview are terminal for automatic advancement.
        Assert.Equal(4, progress.CompletedSteps);
        Assert.Equal(50, progress.Percent);
    }

    [Fact]
    public void Build_TreatsNeedsReviewAsTerminalButKeepsItVisibleForHumanRecovery()
    {
        var progress = CrmProspectingRunProgressBuilder.Build(Rows("NeedsReview", "NeedsReview"));

        Assert.Equal(2, progress.CompletedSteps);
        Assert.Equal(2, progress.NeedsReviewSteps);
        Assert.Equal(100, progress.Percent);
    }

    [Fact]
    public void Build_TruncatesPercentInsteadOfRoundingPastTheTerminalCount()
    {
        // 2 of 3 terminal -> 66.66..%; integer truncation keeps the bar from claiming 67%.
        var progress = CrmProspectingRunProgressBuilder.Build(Rows("Succeeded", "Succeeded", "Running"));

        Assert.Equal(2, progress.CompletedSteps);
        Assert.Equal(66, progress.Percent);
    }

    [Fact]
    public void Build_NeverCountsUnknownStatesAsRunningOrCompleted()
    {
        var progress = CrmProspectingRunProgressBuilder.Build(Rows("SomethingNew", "Succeeded"));

        Assert.Equal(2, progress.TotalSteps);
        Assert.Equal(0, progress.RunningSteps);
        Assert.Equal(1, progress.CompletedSteps);
        Assert.Equal(50, progress.Percent);
    }

    [Fact]
    public void Build_IsPureOverTheProvidedRows()
    {
        var rows = Rows("Succeeded");
        var first = CrmProspectingRunProgressBuilder.Build(rows);
        var second = CrmProspectingRunProgressBuilder.Build(rows);

        Assert.Equal(first.Percent, second.Percent);
        Assert.Equal(rows.Count, first.TotalSteps);
    }

    [Fact]
    public void Build_RejectsANullRowSet()
    {
        Assert.Throws<ArgumentNullException>(() => CrmProspectingRunProgressBuilder.Build(null!));
    }

    private static IReadOnlyList<SysCrmProspectingRunProviderExecution> Rows(params string[] states)
        => states
            .Select(state => new SysCrmProspectingRunProviderExecution { State = state })
            .ToArray();
}
