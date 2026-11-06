using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingExtractionPersistenceContractTests
{
    [Fact]
    public void ExtractionJob_ShouldBeStrictlyTenantScoped()
        => Assert.Contains(typeof(IStrictMultiTenantEntity),
            typeof(SysCrmProspectingExtractionJob).GetInterfaces());

    [Fact]
    public void Repository_ShouldExposeDurableClaimExpiryAndCursorOperations()
    {
        var methods = typeof(ICrmProspectingExtractionJobRepository).GetMethods()
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("CreateSubmissionIntentAsync", methods);
        var directCreate = typeof(ICrmProspectingExtractionJobRepository)
            .GetMethod("CreateAsync");
        Assert.NotNull(directCreate);
        Assert.NotNull(Attribute.GetCustomAttribute(directCreate!, typeof(ObsoleteAttribute)));
        Assert.Contains("TryAcknowledgeSubmissionAsync", methods);
        Assert.Contains("GetByIdAsync", methods);
        Assert.Contains("ClaimDueAsync", methods);
        Assert.Contains("TryRenewLeaseAsync", methods);
        Assert.Contains("ExpireDueAsync", methods);
        Assert.Contains("TryTransitionAsync", methods);
        Assert.Contains("TryCompleteResultsAsync", methods);
        Assert.Contains("TryAdvanceCursorAsync", methods);
    }

    [Fact]
    public void ExtractionLeaseRenewal_ShouldRequireExactJobLeaseAndFencingIdentity()
    {
        var method = typeof(ICrmProspectingExtractionJobRepository).GetMethod("TryRenewLeaseAsync");
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(
            new[] { typeof(long), typeof(Guid), typeof(long), typeof(TimeSpan), typeof(CancellationToken) },
            parameters.Select(parameter => parameter.ParameterType).ToArray());
        Assert.Equal(typeof(Task<bool>), method.ReturnType);
    }

    [Fact]
    public void RemoteProviderStatuses_ShouldNotBePersistedLifecycleStatuses()
    {
        Assert.NotEqual(
            typeof(CrmProspectingExtractionStatus),
            typeof(CrmProspectingExtractionProviderStatus));
        Assert.DoesNotContain(
            Enum.GetNames<CrmProspectingExtractionProviderStatus>(),
            name => name == nameof(CrmProspectingExtractionStatus.FetchingResults)
                || name == nameof(CrmProspectingExtractionStatus.Completed));
    }

    [Fact]
    public void ExtractionJob_ShouldCarryTenantRunFingerprintAndFencingFields()
    {
        var properties = typeof(SysCrmProspectingExtractionJob).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(SysCrmProspectingExtractionJob.TenantId), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.RunId), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.RequestFingerprint), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.SubmissionIdempotencyKey), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.ProviderExtractionId), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.NextPollAtUtc), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.ResultCursor), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.LeaseToken), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.FencingVersion), properties);
    }

    [Fact]
    public void CandidatePersistence_ShouldExposeAtomicProviderBatchAndTerminalTransitionBoundary()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingCandidateFactory.cs"));
        var methodStart = source.IndexOf("PersistProviderBatchAndTransitionAsync", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodEnd = source.IndexOf("PersistPageAndAdvanceAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.Contains("_providerExecutions.GetByRunAsync", method, StringComparison.Ordinal);
        Assert.Contains("execution.CheckpointHash", method, StringComparison.Ordinal);
        Assert.Contains("PersistCandidateOrRepairAsync", method, StringComparison.Ordinal);
        Assert.Contains("\"Projecting\", \"Succeeded\"", method, StringComparison.Ordinal);
        Assert.Contains("\"Projecting\", \"Failed\"", method, StringComparison.Ordinal);
        Assert.Contains("unitOfWork.CompleteAsync", method, StringComparison.Ordinal);
        Assert.Contains("_providerExecutions", source, StringComparison.Ordinal);
    }
    [Fact]
    public void AtomicProviderBatch_ShouldRejectDuplicateNormalizedRecordIdentitiesBeforePersistence()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingCandidateFactory.cs"));
        var methodStart = source.IndexOf("PersistProviderBatchAndTransitionAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("PersistPageAndAdvanceAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.Contains("new HashSet<string>(StringComparer.Ordinal)", method, StringComparison.Ordinal);
        Assert.Contains("if (!batchIdentities.Add(normalizedIdentity))", method, StringComparison.Ordinal);
        Assert.True(method.IndexOf("if (!batchIdentities.Add(normalizedIdentity))", StringComparison.Ordinal) <
            method.IndexOf("await PersistCandidateOrRepairAsync(item, cancellationToken)", StringComparison.Ordinal));
    }

    [Fact]
    public void AtomicProviderBatch_ShouldVerifyCheckpointBytesHashAndRecordMembership()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingCandidateFactory.cs"));
        var methodStart = source.IndexOf("PersistProviderBatchAndTransitionAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("PersistPageAndAdvanceAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.Contains("execution.CheckpointBytes != System.Text.Encoding.UTF8.GetByteCount(execution.CheckpointJson)", method, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(execution.CheckpointJson))", method, StringComparison.Ordinal);
        Assert.Contains("persistedCheckpoint.Records.Count != execution.RawRecordCount", method, StringComparison.Ordinal);
        Assert.Contains("if (!checkpointIdentities.Contains(normalizedIdentity))", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SubmissionIntent_ShouldNotQueryInsideAnAbortedUniqueViolationTransaction()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingExtractionJobRepository.cs"));
        var methodStart = source.IndexOf("CreateSubmissionIntentAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("TryAcknowledgeSubmissionAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("await AddAsync(entity)", method, StringComparison.Ordinal);
        Assert.Contains("unique index is the concurrency boundary", method, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (SqlSugarException)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("var raced = await CreateQueryable()", method, StringComparison.Ordinal);
    }

    [Fact]
    public void RunCompletion_ShouldRemainBlockedByNeedsReviewExtractionJobs()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingRunRepository.cs"));
        var methodStart = source.IndexOf("CompleteFencedWithDatabaseClockAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("CancelFencedWithDatabaseClockAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("pending_job.status NOT IN", method, StringComparison.Ordinal);
        Assert.Contains("@completedExtractionStatus", method, StringComparison.Ordinal);
        Assert.Contains("@failedExtractionStatus", method, StringComparison.Ordinal);
        Assert.Contains("@expiredExtractionStatus", method, StringComparison.Ordinal);
        Assert.Contains("@cancelledExtractionStatus", method, StringComparison.Ordinal);
        Assert.DoesNotContain("@needsReviewExtractionStatus", method, StringComparison.Ordinal);
        Assert.DoesNotContain("CrmProspectingExtractionStatus.NeedsReview.ToString()", method, StringComparison.Ordinal);
    }


    [Fact]
    public void SubmissionAcknowledgement_ShouldPersistValidatedSafeMetadata()
    {
        var method = typeof(ICrmProspectingExtractionJobRepository).GetMethod("TryAcknowledgeSubmissionAsync");
        Assert.NotNull(method);
        var metadataParameter = method!.GetParameters().Single(parameter => parameter.Name == "safeMetadataJson");
        Assert.Equal(typeof(string), metadataParameter.ParameterType);
        Assert.True(metadataParameter.IsOptional);

        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingExtractionJobRepository.cs"));
        var methodStart = source.IndexOf("TryAcknowledgeSubmissionAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("[Obsolete", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var acknowledgement = source[methodStart..methodEnd];

        Assert.Contains("nextPollAtUtc - submittedAtUtc, safeMetadataJson", acknowledgement, StringComparison.Ordinal);
        Assert.Contains("safe_metadata_json = @safeMetadataJson", acknowledgement, StringComparison.Ordinal);
        Assert.Contains("new SugarParameter(\"@safeMetadataJson\", safeMetadataJson)", acknowledgement, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingExtractionSubmissionValidator.TryValidate", acknowledgement, StringComparison.Ordinal);
    }



    [Fact]
    public void ExtractionExpiryWorker_ShouldSweepEachTenantWithoutClaimingProviderWork()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting",
            "CrmProspectingExtractionExpiryHostedService.cs"));

        Assert.Contains("ITenantDataScopeRunner", source, StringComparison.Ordinal);
        Assert.Contains("if (tenantId is null) return;", source, StringComparison.Ordinal);
        Assert.Contains("ICrmProspectingExtractionJobRepository", source, StringComparison.Ordinal);
        Assert.Contains("ExpireDueAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimDueAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ICrmProspectingAsyncExtractionProvider", source, StringComparison.Ordinal);
        Assert.Contains("config.UpdateMaxConcurrentTasks(1)", source, StringComparison.Ordinal);
    }



    [Fact]
    public void ExtractionExpiry_ShouldNotGuessOutcomeForSubmittingOrNeedsReviewJobs()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories",
            "CrmProspectingExtractionJobRepository.cs"));
        var methodStart = source.IndexOf("ExpireDueAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("TryFailIncompleteResultsAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("status IN (@pendingStatus, @runningStatus, @succeededStatus, @fetchingStatus)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("@submittingStatus", method, StringComparison.Ordinal);
        Assert.DoesNotContain("@needsReviewStatus", method, StringComparison.Ordinal);
        Assert.Contains("expires_at_utc <= clock_timestamp()", method, StringComparison.Ordinal);
        Assert.Contains("lease_token = NULL", method, StringComparison.Ordinal);
    }


    [Fact]
    public void NeedsReviewRecovery_ShouldRequireAResolvedProviderIdentityAndRemainManual()
    {
        Assert.False(CrmProspectingExtractionStateMachine.RequiresProviderExtractionId(
            CrmProspectingExtractionStatus.NeedsReview));
        Assert.True(CrmProspectingExtractionStateMachine.RequiresProviderExtractionId(
            CrmProspectingExtractionStatus.Pending));
        Assert.True(CrmProspectingExtractionStateMachine.RequiresProviderExtractionId(
            CrmProspectingExtractionStatus.Running));

        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingExtractionJobRepository.cs"));

        var transitionStart = source.IndexOf("TryTransitionAsync", StringComparison.Ordinal);
        var cursorStart = source.IndexOf("TryAdvanceCursorAsync", transitionStart, StringComparison.Ordinal);
        Assert.True(transitionStart >= 0 && cursorStart > transitionStart);
        var transition = source[transitionStart..cursorStart];
        Assert.Contains("CrmProspectingExtractionStateMachine.RequiresProviderExtractionId(nextStatus)", transition, StringComparison.Ordinal);
        Assert.Contains("@requiresProviderExtractionId = FALSE OR provider_extraction_id IS NOT NULL", transition, StringComparison.Ordinal);

        var claimStart = source.IndexOf("ClaimDueAsync", StringComparison.Ordinal);
        var renewStart = source.IndexOf("TryRenewLeaseAsync", claimStart, StringComparison.Ordinal);
        Assert.True(claimStart >= 0 && renewStart > claimStart);
        var claim = source[claimStart..renewStart];
        Assert.DoesNotContain("NeedsReview", claim, StringComparison.Ordinal);
        Assert.Contains("job.status IN (@submittingStatus, @pendingStatus, @runningStatus, @succeededStatus, @fetchingStatus)", claim, StringComparison.Ordinal);
    }

    [Fact]
    public void NeedsReviewManualReconciliation_ShouldRequireIdentityAuditAndAtomicTenantScopedResolution()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "docs", "kiger",
            "PROVIDER-ASYNC-EXTRACTION-DESIGN.md")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var document = File.ReadAllText(Path.Combine(tree!.FullName, "docs", "kiger",
            "PROVIDER-ASYNC-EXTRACTION-DESIGN.md"));
        var sectionStart = document.IndexOf("## 11.58 Manual NeedsReview reconciliation protocol", StringComparison.Ordinal);
        Assert.True(sectionStart >= 0);
        var section = document[sectionStart..];

        Assert.Contains("authenticated request context", section, StringComparison.Ordinal);
        Assert.Contains("operator subject ID", section, StringComparison.Ordinal);
        Assert.Contains("provider_extraction_id IS NULL", section, StringComparison.Ordinal);
        Assert.Contains("immutable reconciliation audit record", section, StringComparison.Ordinal);
        Assert.Contains("If audit insertion fails, roll back the state transition", section, StringComparison.Ordinal);
        Assert.Contains("concurrent operator race", section, StringComparison.Ordinal);
        Assert.Contains("Do not expose a state-only", section, StringComparison.Ordinal);
    }
}
