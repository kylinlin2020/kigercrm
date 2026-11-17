using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// W4 Run pause/resume source-level guards. These lock the six concurrency decisions finalized in
/// docs/kiger/AUTO-PROSPECTING-W4-IMPLEMENTATION-2026-11-17.md so a future edit cannot silently
/// reintroduce terminal settlement during a pause or resurrect a Paused Run.
/// </summary>
public sealed class CrmProspectingPauseResumeContractTests
{
    [Fact]
    public void RunStatus_ShouldAddPausedAsAnAdditiveNonTerminalValue()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Domain", "Entities", "Enums", "CrmProspectingRunStatus.cs");
        Assert.Contains("WaitingForExtractions = 7", source, StringComparison.Ordinal);
        Assert.Contains("Paused = 8", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TryPause_ShouldOnlyAcceptActiveStatesAndPreserveLease()
    {
        var repo = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        var method = Slice(repo,
            "public async Task<SysCrmProspectingRun?> TryPauseAsync",
            "public async Task<bool> ReleaseLeaseForPauseAsync");

        Assert.Contains("run.tenant_id = @tenantId", method, StringComparison.Ordinal);
        Assert.Contains("run.status IN (@queuedStatus, @runningStatus, @waitingStatus)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("lease_token = NULL", method, StringComparison.Ordinal);
        Assert.DoesNotContain("completed_at", method, StringComparison.Ordinal);
        Assert.Contains("RETURNING run.*", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PauseExit_ShouldReleaseLeaseUnderExactFencingWithoutTerminalSettlement()
    {
        var repo = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        var method = Slice(repo,
            "public async Task<bool> ReleaseLeaseForPauseAsync",
            "public async Task<SysCrmProspectingRun?> TryResumeAsync");

        Assert.Contains("run.status = @pausedStatus", method, StringComparison.Ordinal);
        Assert.Contains("run.lease_token = @leaseToken", method, StringComparison.Ordinal);
        Assert.Contains("run.fencing_version = @fencingVersion", method, StringComparison.Ordinal);
        Assert.Contains("fencing_version = run.fencing_version + 1", method, StringComparison.Ordinal);
        Assert.Contains("lease_token = NULL", method, StringComparison.Ordinal);
        Assert.DoesNotContain("completed_at", method, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResume_ShouldOnlyAcceptPausedAndDeriveTargetFromExtractionLedger()
    {
        var repo = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        var method = Slice(repo,
            "public async Task<SysCrmProspectingRun?> TryResumeAsync",
            "public async Task<bool> HasExtractionJobsAsync");

        Assert.Contains("run.status = @pausedStatus", method, StringComparison.Ordinal);
        Assert.Contains("resumeToWaiting", method, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunStatus.WaitingForExtractions", method, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunStatus.Queued", method, StringComparison.Ordinal);
        Assert.Contains("fencing_version = run.fencing_version + 1", method, StringComparison.Ordinal);
    }

    [Fact]
    public void InFlightWriteAndHeartbeat_ShouldAdmitPausedButTerminalSettersShouldNot()
    {
        var repo = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");

        var writeFence = Slice(repo,
            "public async Task<bool> TryLockLeaseForWriteAsync",
            "public async Task<SysCrmProspectingRun?> ClaimQueuedAsync");
        Assert.Contains("run.status IN (@runningStatus, @pausedStatus)", writeFence, StringComparison.Ordinal);

        var heartbeat = Slice(repo,
            "public async Task<bool> RenewLeaseWithDatabaseClockAsync",
            "public async Task<bool> RenewLeaseAsync");
        Assert.Contains("run.status IN (@runningStatus, @waitingStatus, @pausedStatus)", heartbeat, StringComparison.Ordinal);

        var completeFenced = Slice(repo,
            "public async Task<bool> CompleteFencedWithDatabaseClockAsync",
            "public async Task<bool> CancelFencedWithDatabaseClockAsync");
        Assert.DoesNotContain("@pausedStatus", completeFenced, StringComparison.Ordinal);
        var failFenced = Slice(repo,
            "public async Task<bool> FailFencedWithDatabaseClockAsync",
            "public async Task<bool> CompleteFencedAsync");
        Assert.DoesNotContain("@pausedStatus", failFenced, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryPaths_ShouldNotResurrectPausedRuns()
    {
        var repo = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");

        var expired = Slice(repo,
            "public async Task<int> RecoverExpiredLeasesAsync",
            "public async Task<bool> RecoverInterruptedAsync");
        Assert.Contains("run.status IN (@runningStatus, @cancelRequestedStatus, @waitingStatus)", expired, StringComparison.Ordinal);
        Assert.DoesNotContain("pausedStatus", expired, StringComparison.Ordinal);

        var interrupted = Slice(repo,
            "public async Task<bool> RecoverInterruptedAsync",
            "public async Task<bool> CompleteAsync");
        Assert.Contains("CrmProspectingRunStatus.Running", interrupted, StringComparison.Ordinal);
        Assert.DoesNotContain("CrmProspectingRunStatus.Paused", interrupted, StringComparison.Ordinal);

        var queued = Slice(repo,
            "public async Task<IReadOnlyList<long>> GetQueuedIdsAsync",
            "public async Task<int> RecoverExpiredLeasesAsync");
        Assert.Contains("run.Status == CrmProspectingRunStatus.Queued", queued, StringComparison.Ordinal);
        Assert.DoesNotContain("Paused", queued, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_ShouldUseDedicatedPauseExitAtEveryCheckpoint()
    {
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs");

        Assert.Contains("private sealed class RunPausedException", worker, StringComparison.Ordinal);
        Assert.Contains("catch (RunPausedException)", worker, StringComparison.Ordinal);
        Assert.Contains("ReleaseLeaseForPauseAsync", worker, StringComparison.Ordinal);
        var throwCount = worker.Split("throw new RunPausedException()").Length - 1;
        Assert.True(throwCount >= 5, $"Expected >= 5 pause checkpoints, found {throwCount}");
        var pauseCatch = Slice(worker, "catch (RunPausedException)", "catch (Exception ex)");
        Assert.DoesNotContain("CompleteFenced", pauseCatch, StringComparison.Ordinal);
        Assert.DoesNotContain("FailFenced", pauseCatch, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelFenced", pauseCatch, StringComparison.Ordinal);
    }

    [Fact]
    public void DomainCancel_ShouldSendPausedStraightToCancelled()
    {
        var domain = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Domain", "DomainServices", "Implementations", "CrmProspectingDomainService.cs");
        var method = Slice(domain,
            "public async Task<SysCrmProspectingRun> CancelRunAsync",
            "private async Task<SysCrmProspectingStrategy> GetStrategyAsync");
        Assert.Contains("run.Status == CrmProspectingRunStatus.Paused", method, StringComparison.Ordinal);
        Assert.Contains("run.Status = CrmProspectingRunStatus.Cancelled", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationContract_ShouldExposePauseAndResumeWithExecutePermission()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "AppServices", "CrmProspectingAppService.cs");
        Assert.Contains("public async Task<CrmProspectingRunDto> PauseRunAsync", app, StringComparison.Ordinal);
        Assert.Contains("public async Task<CrmProspectingRunDto> ResumeRunAsync", app, StringComparison.Ordinal);

        Assert.True(PrecededByExecutePermission(app, "public async Task<CrmProspectingRunDto> PauseRunAsync"));
        Assert.True(PrecededByExecutePermission(app, "public async Task<CrmProspectingRunDto> ResumeRunAsync"));
        var pause = Slice(app, "public async Task<CrmProspectingRunDto> PauseRunAsync", "public async Task<CrmProspectingRunDto> ResumeRunAsync");
        var resume = Slice(app, "public async Task<CrmProspectingRunDto> ResumeRunAsync", "[PermissionAuthorize(CrmPermissionCodes.Read)]");
        Assert.Contains("TryPauseAsync", pause, StringComparison.Ordinal);
        Assert.Contains("HasExtractionJobsAsync", resume, StringComparison.Ordinal);
        Assert.Contains("TryResumeAsync", resume, StringComparison.Ordinal);
        Assert.Contains("runQueue.EnqueueAsync", resume, StringComparison.Ordinal);

        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "Contracts", "ICrmProspectingAppService.cs");
        Assert.Contains("Task<CrmProspectingRunDto> PauseRunAsync", contract, StringComparison.Ordinal);
        Assert.Contains("Task<CrmProspectingRunDto> ResumeRunAsync", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendContract_ShouldExposePauseResumeAndPausedStatusLabel()
    {
        var types = Read("frontend", "src", "api", "modules", "crm", "prospecting.types.ts");
        Assert.Contains("Paused = 8", types, StringComparison.Ordinal);

        var api = Read("frontend", "src", "api", "modules", "crm", "prospecting.ts");
        Assert.Contains("pauseRun(runId", api, StringComparison.Ordinal);
        Assert.Contains("resumeRun(runId", api, StringComparison.Ordinal);
        Assert.Contains("'PauseRun'", api, StringComparison.Ordinal);
        Assert.Contains("'ResumeRun'", api, StringComparison.Ordinal);

        var index = Read("frontend", "src", "modules", "crm", "views", "crm", "prospecting", "index.vue");
        Assert.Contains("run.status === CrmProspectingRunStatus.Paused", index, StringComparison.Ordinal);
        Assert.Contains("crm.prospecting.runPaused", index, StringComparison.Ordinal);
        var polling = Slice(index, "const shouldPollRun = computed", "useRunMonitorPolling");
        Assert.DoesNotContain("Paused", polling, StringComparison.Ordinal);

        var monitor = Read("frontend", "src", "modules", "crm", "views", "crm", "prospecting", "RunMonitorPanel.vue");
        Assert.Contains("crm.prospecting.runPaused", monitor, StringComparison.Ordinal);

        foreach (var lang in new[] { "en-US", "zh-CN", "zh-TW", "ja-JP", "ko-KR", "hi-IN", "de-DE" })
        {
            var locale = Read("frontend", "src", "modules", "crm", "locales", lang + ".ts");
            Assert.Contains("runPaused:", locale, StringComparison.Ordinal);
            Assert.Contains("pauseRun:", locale, StringComparison.Ordinal);
            Assert.Contains("resumeRun:", locale, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void InFlightFencedWrites_ShouldAdmitPausedAsAnActiveRunState()
    {
        // Pause preserves the lease so an already-dispatched provider call can still land its
        // checkpoint. Every fenced write gated on the parent Run must therefore accept Paused;
        // otherwise a Pause arriving mid-write fails the write and strands the Run. This was a
        // real defect found by CrmProspectingPauseResumeWorkerIntegrationTests.
        var executionRepo = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunProviderExecutionRepository.cs");
        Assert.Equal(5, Count(executionRepo, "run.status IN (@runningStatus, @pausedStatus)"));
        Assert.Equal(5, Count(executionRepo, "SugarParameter(\"@pausedStatus\""));
        Assert.DoesNotContain("run.status = @runningStatus", executionRepo, StringComparison.Ordinal);

        var requestRepo = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingProviderRequestRepository.cs");
        Assert.Contains("run.status IN (@runningStatus, @pausedStatus)", requestRepo, StringComparison.Ordinal);
        // A paused Run keeps its lease; stale-attempt recovery must not treat it as abandoned.
        Assert.Contains("run.status NOT IN (@runningStatus, @pausedStatus, @cancelRequestedStatus)",
            requestRepo, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderStepTransition_ShouldCastNullableIntParametersForPostgres()
    {
        // Npgsql infers a null int? bound parameter as text; COALESCE against an integer column
        // then fails with 42804. The Checkpointed->Projecting replay path passes null and must cast.
        var repo = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunProviderExecutionRepository.cs");
        Assert.Contains("COALESCE(CAST(@projectedRecordCount AS INTEGER), step.projected_record_count)",
            repo, StringComparison.Ordinal);
        Assert.Contains("COALESCE(CAST(@failureCount AS INTEGER), step.failure_count)",
            repo, StringComparison.Ordinal);
    }

    private static int Count(string source, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    private static bool PrecededByExecutePermission(string source, string methodSignature)
    {
        var index = source.IndexOf(methodSignature, StringComparison.Ordinal);
        if (index < 0) return false;
        var window = source[Math.Max(0, index - 240)..index];
        return window.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing start marker: " + startMarker);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, "Missing end marker: " + endMarker);
        return source[start..end];
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
