using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Source-level guards for the parent Run side of asynchronous extraction orchestration.</summary>
public sealed class CrmProspectingRunOrchestrationContractTests
{
    [Fact]
    public void WaitingForExtractions_ShouldBeAnAdditivePersistedRunState()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Domain", "Entities", "Enums", "CrmProspectingRunStatus.cs");

        Assert.Contains("Queued = 0", source, StringComparison.Ordinal);
        Assert.Contains("Cancelled = 6", source, StringComparison.Ordinal);
        Assert.Contains("WaitingForExtractions = 7", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitingRunDiscovery_ShouldBeTenantScopedBoundedAndDeterministic()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        var start = source.IndexOf(
            "public async Task<IReadOnlyList<long>> GetWaitingForExtractionsIdsAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "public async Task<SysCrmProspectingRun?> TryClaimWaitingForExtractionsLeaseAsync",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("run.tenant_id = @tenantId", method, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunStatus.WaitingForExtractions", method, StringComparison.Ordinal);
        Assert.Contains("EXISTS (", method, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS (", method, StringComparison.Ordinal);
        Assert.Contains("@completedStatus", method, StringComparison.Ordinal);
        Assert.Contains("@failedStatus", method, StringComparison.Ordinal);
        Assert.Contains("@expiredStatus", method, StringComparison.Ordinal);
        Assert.Contains("@cancelledStatus", method, StringComparison.Ordinal);
        Assert.Contains("ORDER BY run.basic_id", method, StringComparison.Ordinal);
        Assert.Contains("run.lease_expires_at <= clock_timestamp()", method, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit", method, StringComparison.Ordinal);
        Assert.Contains("limit is < 1 or > 1000", method, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitingRunClaim_ShouldUseExactTenantAndDatabaseClockFencing()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        var start = source.IndexOf(
            "public async Task<SysCrmProspectingRun?> TryClaimWaitingForExtractionsLeaseAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "public async Task<IReadOnlyList<SysCrmProspectingRun>> GetLatestByCampaignAsync",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("DbType.PostgreSQL", method, StringComparison.Ordinal);
        Assert.Contains("run.tenant_id = @tenantId", method, StringComparison.Ordinal);
        Assert.Contains("run.status = @waitingStatus", method, StringComparison.Ordinal);
        Assert.Contains("lease_token = @leaseToken", method, StringComparison.Ordinal);
        Assert.Contains("fencing_version = run.fencing_version + 1", method, StringComparison.Ordinal);
        Assert.Contains("clock_timestamp()", method, StringComparison.Ordinal);
        Assert.Contains("RETURNING run.*", method, StringComparison.Ordinal);
        Assert.Contains("lease_owner IS NULL", method, StringComparison.Ordinal);
        Assert.Contains("lease_token IS NULL", method, StringComparison.Ordinal);
        Assert.Contains("lease_expires_at IS NULL", method, StringComparison.Ordinal);
        Assert.Contains("lease_heartbeat_at IS NULL", method, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryRunQueueClaim_ShouldNotClaimWaitingForExtractions()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        var start = source.IndexOf(
            "public async Task<SysCrmProspectingRun?> TryClaimLeaseAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "public async Task<bool> TryLockLeaseForWriteAsync",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("@queuedStatus", method, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitingForExtractions", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinatorLease_ShouldLeaveRunOutOfOrdinaryQueueLifecycle()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        var claimStart = source.IndexOf(
            "public async Task<SysCrmProspectingRun?> TryClaimWaitingForExtractionsLeaseAsync",
            StringComparison.Ordinal);
        var claimEnd = source.IndexOf(
            "public async Task<IReadOnlyList<SysCrmProspectingRun>> GetLatestByCampaignAsync",
            claimStart,
            StringComparison.Ordinal);
        Assert.True(claimStart >= 0 && claimEnd > claimStart);
        var claim = source[claimStart..claimEnd];
        Assert.Contains("SET status = @waitingStatus", claim, StringComparison.Ordinal);
        Assert.DoesNotContain("SET status = @runningStatus", claim, StringComparison.Ordinal);
        Assert.Contains("run.lease_expires_at <= lease_clock.now", claim, StringComparison.Ordinal);

        var recoveryStart = source.IndexOf(
            "public async Task<int> RecoverExpiredLeasesAsync",
            StringComparison.Ordinal);
        var recoveryEnd = source.IndexOf(
            "public async Task<bool> RecoverInterruptedAsync",
            recoveryStart,
            StringComparison.Ordinal);
        Assert.True(recoveryStart >= 0 && recoveryEnd > recoveryStart);
        var recovery = source[recoveryStart..recoveryEnd];
        Assert.Contains("WHEN run.status = @waitingStatus THEN @waitingStatus", recovery, StringComparison.Ordinal);
        Assert.Contains("@runningStatus, @cancelRequestedStatus, @waitingStatus", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void FencedCompletion_ShouldAcceptWaitingParentButStillBlockNonterminalChildren()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        var start = source.IndexOf(
            "public async Task<bool> CompleteFencedWithDatabaseClockAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "public async Task<bool> CancelFencedWithDatabaseClockAsync",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("run.status IN (@runningStatus, @waitingStatus)", method, StringComparison.Ordinal);
        Assert.Contains("pending_job.status NOT IN", method, StringComparison.Ordinal);
        Assert.Contains("@completedExtractionStatus", method, StringComparison.Ordinal);
        Assert.Contains("@failedExtractionStatus", method, StringComparison.Ordinal);
        Assert.Contains("@expiredExtractionStatus", method, StringComparison.Ordinal);
        Assert.Contains("@cancelledExtractionStatus", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Coordinator_ShouldWaitForEveryChildAndPreserveNeedsReviewGate()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Prospecting", "CrmProspectingExtractionCoordinatorHostedService.cs");

        Assert.Contains("children.Count == 0", source, StringComparison.Ordinal);
        Assert.Contains("children.Any(job => !IsTerminal(job.Status))", source, StringComparison.Ordinal);
        Assert.Contains("nameof(CrmProspectingExtractionStatus.Completed)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(CrmProspectingExtractionStatus.Failed)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(CrmProspectingExtractionStatus.Expired)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(CrmProspectingExtractionStatus.Cancelled)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(CrmProspectingExtractionStatus.NeedsReview)", source, StringComparison.Ordinal);
        Assert.Contains("run.LeaseToken != message.LeaseToken", source, StringComparison.Ordinal);
        Assert.Contains("run.FencingVersion != message.FencingVersion", source, StringComparison.Ordinal);
        Assert.Contains("CompleteFencedWithDatabaseClockAsync", source, StringComparison.Ordinal);
        Assert.Contains("ResultRecordCount < 0", source, StringComparison.Ordinal);
        Assert.Contains("rawRecordCountTotal > int.MaxValue", source, StringComparison.Ordinal);
        Assert.Contains("FailFencedWithDatabaseClockAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitingParentCancellation_ShouldInvalidateCoordinatorLease()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Domain", "DomainServices", "Implementations", "CrmProspectingDomainService.cs");
        var start = source.IndexOf(
            "public async Task<SysCrmProspectingRun> CancelRunAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf("private async Task<SysCrmProspectingStrategy>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("CrmProspectingRunStatus.WaitingForExtractions", method, StringComparison.Ordinal);
        Assert.Contains("run.Status = CrmProspectingRunStatus.Cancelled", method, StringComparison.Ordinal);
        Assert.Contains("run.LeaseToken = null", method, StringComparison.Ordinal);
        Assert.Contains("run.FencingVersion = checked(run.FencingVersion + 1)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinatorHostedService_ShouldBeRegisteredWithCrmRuntime()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Extensions", "ServiceCollectionExtensions.cs");
        Assert.Contains("AddHostedService<CrmProspectingExtractionCoordinatorHostedService>()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncRunCreation_ShouldPersistChildIntentsBeforeCommitAndSkipNormalQueue()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "AppServices", "CrmProspectingAppService.cs");
        var start = source.IndexOf(
            "private async Task<CrmProspectingRunDto> StartRunInUnitOfWorkAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private static string[] ParseProviderPlan",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        var begin = method.IndexOf("isTransactional: true", StringComparison.Ordinal);
        var createRun = method.IndexOf("domain.CreateRunAsync", StringComparison.Ordinal);
        var moveToWaiting = method.IndexOf("TrySetWaitingForExtractionsAsync", StringComparison.Ordinal);
        var createIntent = method.IndexOf("CreateSubmissionIntentAsync", StringComparison.Ordinal);
        var commit = method.IndexOf("uow.CompleteAsync", StringComparison.Ordinal);
        Assert.True(begin >= 0 && createRun > begin);
        Assert.True(moveToWaiting > createRun);
        Assert.True(createIntent > moveToWaiting);
        Assert.True(commit > createIntent);
        Assert.Contains("Mixed synchronous/asynchronous provider plans remain unsupported", method, StringComparison.Ordinal);
        Assert.Contains("uow.OnCompleted(() => runQueue.EnqueueAsync", method, StringComparison.Ordinal);
        Assert.Contains("if (asyncCodes.Length > 0)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncRunTransition_ShouldRequireTheCreationTransaction()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs");
        var start = source.IndexOf(
            "public async Task<bool> TrySetWaitingForExtractionsAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "public async Task<IReadOnlyList<long>> GetWaitingForExtractionsIdsAsync",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("DbType.PostgreSQL", method, StringComparison.Ordinal);
        Assert.Contains("client.Ado.Transaction is null", method, StringComparison.Ordinal);
        Assert.Contains("run.status = @queuedStatus", method, StringComparison.Ordinal);
        Assert.Contains("lease_owner IS NULL", method, StringComparison.Ordinal);
        Assert.Contains("lease_token IS NULL", method, StringComparison.Ordinal);
        Assert.Contains("lease_expires_at IS NULL", method, StringComparison.Ordinal);
        Assert.Contains("lease_heartbeat_at IS NULL", method, StringComparison.Ordinal);
        Assert.Contains("RETURNING basic_id", method, StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncExecution_ShouldRemainFeatureFlaggedOffByDefault()
    {
        var source = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "AppServices", "CrmProspectingAppService.cs");
        Assert.Contains("CrmProspecting:AsyncExecution:Enabled", source, StringComparison.Ordinal);
        Assert.Contains("configuration[\"CrmProspecting:AsyncExecution:Enabled\"]", source, StringComparison.Ordinal);
        Assert.Contains("Asynchronous prospecting execution is disabled", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
