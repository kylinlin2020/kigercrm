// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

/// <summary>
/// Pure aggregation over durable provider execution checkpoints. Unknown states are counted
/// nowhere: the projection reports what the ledger actually says instead of fabricating progress.
/// </summary>
public static class CrmProspectingRunProgressBuilder
{
    public const string QueuedState = "Queued";
    public const string StartedState = "Started";
    public const string CheckpointedState = "Checkpointed";
    public const string ProjectingState = "Projecting";
    public const string SucceededState = "Succeeded";
    public const string FailedState = "Failed";
    public const string NeedsReviewState = "NeedsReview";
    public const string SkippedState = "Skipped";

    public static CrmProspectingRunProgressDto Build(IReadOnlyList<SysCrmProspectingRunProviderExecution> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var progress = new CrmProspectingRunProgressDto { TotalSteps = rows.Count };
        foreach (var row in rows)
        {
            switch (row.State)
            {
                case SucceededState:
                    progress.SucceededSteps++;
                    break;
                case FailedState:
                    progress.FailedSteps++;
                    break;
                case StartedState:
                case CheckpointedState:
                case ProjectingState:
                    progress.RunningSteps++;
                    break;
                case QueuedState:
                    progress.QueuedSteps++;
                    break;
                case NeedsReviewState:
                    progress.NeedsReviewSteps++;
                    break;
                case SkippedState:
                    progress.SkippedSteps++;
                    break;
            }
        }

        progress.CompletedSteps = progress.SucceededSteps + progress.FailedSteps
            + progress.SkippedSteps + progress.NeedsReviewSteps;
        progress.Percent = progress.TotalSteps == 0
            ? 0
            : progress.CompletedSteps * 100 / progress.TotalSteps;
        return progress;
    }
}
