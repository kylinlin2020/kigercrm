using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.Domain.Entities.Abstracts;
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
    public void ExtractionExpiry_ShouldClearEveryLeaseFieldAndFenceStaleWorkers()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingExtractionJobRepository.cs"));
        var methodStart = source.IndexOf("ExpireDueAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("TryFailIncompleteResultsAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("DbType.PostgreSQL", method, StringComparison.Ordinal);
        Assert.Contains("AsyncLocalCurrentTenantAccessor.Instance.Current", method, StringComparison.Ordinal);
        Assert.Contains("WHERE tenant_id = @tenantId", method, StringComparison.Ordinal);
        Assert.Contains("expires_at_utc <= clock_timestamp()", method, StringComparison.Ordinal);
        Assert.Contains("lease_owner = NULL", method, StringComparison.Ordinal);
        Assert.Contains("lease_token = NULL", method, StringComparison.Ordinal);
        Assert.Contains("lease_expires_at_utc = NULL", method, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.UtcNow", method, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", method, StringComparison.Ordinal);
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

    [Fact]
    public void RunWorker_ShouldPreflightAllAsyncProvidersBeforeAnyProviderExecution()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingRunHostedService.cs"));

        var preflight = source.IndexOf("var unsupportedAsyncProviders = plan", StringComparison.Ordinal);
        var rejection = source.IndexOf("if (unsupportedAsyncProviders.Length > 0)", preflight, StringComparison.Ordinal);
        var executionLoop = source.IndexOf("foreach (var providerCode in plan.Distinct", StringComparison.Ordinal);
        Assert.True(preflight >= 0 && rejection > preflight && executionLoop > rejection);
        var guard = source[preflight..executionLoop];
        Assert.Contains("providerRegistry.Asynchronous.ContainsKey(providerCode)", guard, StringComparison.Ordinal);
        Assert.Contains("not yet supported by durable Run orchestration", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitExtractionAsync", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("PollExtractionAsync", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncExtractionWorkerProtocol_ShouldPreserveIdempotencyFencingAndAtomicPageContinuation()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "docs", "kiger",
            "PROVIDER-ASYNC-EXTRACTION-DESIGN.md")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var document = File.ReadAllText(Path.Combine(tree!.FullName, "docs", "kiger",
            "PROVIDER-ASYNC-EXTRACTION-DESIGN.md"));
        var sectionStart = document.IndexOf("## 11.59 Submit/Poll worker execution protocol", StringComparison.Ordinal);
        Assert.True(sectionStart >= 0);
        var section = document[sectionStart..];

        Assert.Contains("same key and only where the provider guarantees", section, StringComparison.Ordinal);
        Assert.Contains("exact lease/fencing identity", section, StringComparison.Ordinal);
        Assert.Contains("GetDefaultPollDelay(nextPollAttemptCount)", section, StringComparison.Ordinal);
        Assert.Contains("Heartbeat must remain active", section, StringComparison.Ordinal);
        Assert.Contains("same Run-level identity boundary", section, StringComparison.Ordinal);
        Assert.Contains("same transaction", section, StringComparison.Ordinal);
        Assert.Contains("NeedsReview remains blocking", section, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL integration tests", section, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractionCursorPersistence_ShouldReuseSharedCursorValidator()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingExtractionJobRepository.cs"));
        var methodStart = source.IndexOf("public async Task<bool> TryAdvanceCursorAsync", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodEnd = source.IndexOf("public async Task<int> ExpireDueAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart);
        var method = source[methodStart..methodEnd];
        Assert.Contains("CrmProspectingExtractionCursorValidator.IsValid(expectedCursor)", method, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingExtractionCursorValidator.IsValid(nextCursor)", method, StringComparison.Ordinal);
    }


    [Fact]
    public void SubmissionIntent_ShouldReusePersistedIdempotencyKeyBeforeGeneratingANewOne()
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

        var existingLookup = method.IndexOf("var existing = await CreateQueryable()", StringComparison.Ordinal);
        var existingReturn = method.IndexOf("if (existing.Count == 1) return existing[0];", StringComparison.Ordinal);
        var newKey = method.IndexOf("SubmissionIdempotencyKey = Guid.NewGuid()", StringComparison.Ordinal);
        Assert.True(existingLookup >= 0 && existingReturn > existingLookup && newKey > existingReturn,
            "A retried local intent must return its original provider idempotency key before any new key can be generated.");
        Assert.Contains("row.TenantId == tenant.TenantId && row.RunId == runId", method, StringComparison.Ordinal);
        Assert.Contains("row.ProviderCode == providerCode && row.RequestFingerprint == fingerprint", method, StringComparison.Ordinal);
    }


    [Fact]
    public void ExtractionFinalization_ShouldReuseSharedCursorValidator()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingExtractionJobRepository.cs"));

        var failedStart = source.IndexOf("public async Task<bool> TryFailIncompleteResultsAsync", StringComparison.Ordinal);
        var completeStart = source.IndexOf("public async Task<bool> TryCompleteResultsAsync", failedStart, StringComparison.Ordinal);
        var transitionStart = source.IndexOf("public async Task<bool> TryTransitionAsync", completeStart, StringComparison.Ordinal);
        Assert.True(failedStart >= 0 && completeStart > failedStart && transitionStart > completeStart);

        var failed = source[failedStart..completeStart];
        var complete = source[completeStart..transitionStart];
        Assert.Contains("CrmProspectingExtractionCursorValidator.IsValid(expectedCursor)", failed, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingExtractionCursorValidator.IsValid(expectedCursor)", complete, StringComparison.Ordinal);
        Assert.DoesNotContain("expectedCursor.Any(char.IsControl)", failed, StringComparison.Ordinal);
        Assert.DoesNotContain("expectedCursor.Any(char.IsControl)", complete, StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncExtractionPagePersistence_ShouldFenceLeaseAndCursorBeforeWritingCandidates()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingCandidateFactory.cs"));
        var start = source.IndexOf("public async Task<int> PersistPageAndAdvanceAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task PersistCandidateOrRepairAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        var transaction = method.IndexOf("isTransactional: true", StringComparison.Ordinal);
        var leaseRead = method.IndexOf("_extractionJobs.GetByIdAsync", StringComparison.Ordinal);
        var leaseFence = method.IndexOf("extraction.FencingVersion != extractionFencingVersion", StringComparison.Ordinal);
        var candidateWrite = method.IndexOf("PersistCandidateOrRepairAsync(item, cancellationToken)", StringComparison.Ordinal);
        var cursorAdvance = method.IndexOf("_extractionJobs.TryAdvanceCursorAsync", StringComparison.Ordinal);
        var commit = method.IndexOf("unitOfWork.CompleteAsync", StringComparison.Ordinal);

        Assert.True(transaction >= 0 && leaseRead > transaction && leaseFence > leaseRead);
        Assert.True(candidateWrite > leaseFence && cursorAdvance > candidateWrite && commit > cursorAdvance);
        Assert.Contains("extraction.LeaseToken != extractionLeaseToken", method, StringComparison.Ordinal);
        Assert.Contains("extraction.ResultCursor, expectedCursor", method, StringComparison.Ordinal);
        Assert.Contains("if (!advanced)", method, StringComparison.Ordinal);
        Assert.Contains("all page writes were rolled back", method, StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncExtractionPagePersistence_ShouldEnforceOneToOneCandidateRecordMembership()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingCandidateFactory.cs"));
        var start = source.IndexOf("public async Task<int> PersistPageAndAdvanceAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task PersistCandidateOrRepairAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("var pageIdentities = page.Records", method, StringComparison.Ordinal);
        Assert.Contains("var itemIdentities = new HashSet<string>(StringComparer.Ordinal)", method, StringComparison.Ordinal);
        Assert.Contains("if (!pageIdentities.Contains(identity) || !itemIdentities.Add(identity))", method, StringComparison.Ordinal);
        Assert.Contains("item.Candidate.RunId != extraction.RunId", method, StringComparison.Ordinal);
        Assert.Contains("item.Candidate.ExternalId, identity", method, StringComparison.Ordinal);
        Assert.Contains("items.Count + page.FailureCount != page.Records.Count", method, StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncExtractionFinalPage_ShouldFailIncompleteResultsInsteadOfReportingSuccess()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmProspectingCandidateFactory.cs"));
        var start = source.IndexOf("public async Task<int> PersistPageAndAdvanceAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task PersistCandidateOrRepairAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        var fail = method.IndexOf("_extractionJobs.TryFailIncompleteResultsAsync", StringComparison.Ordinal);
        var complete = method.IndexOf("_extractionJobs.TryCompleteResultsAsync", StringComparison.Ordinal);
        var continuePage = method.IndexOf("_extractionJobs.TryAdvanceCursorAsync", StringComparison.Ordinal);
        Assert.True(fail >= 0 && complete > fail && continuePage > complete);
        Assert.Contains("page.IsLastPage && extraction.ResultFailureCount + page.FailureCount > 0", method, StringComparison.Ordinal);
        Assert.Contains("page.IsLastPage", method, StringComparison.Ordinal);
        Assert.Contains("expectedCursor: expectedCursor", method, StringComparison.Ordinal);
    }


    [Fact]
    public void EveryExtractionMutation_ShouldFenceTenantLeaseTokenVersionAndDatabaseExpiry()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingExtractionJobRepository.cs"));

        static string MethodBody(string repositorySource, string methodName)
        {
            var start = repositorySource.IndexOf($" {methodName}(", StringComparison.Ordinal);
            Assert.True(start >= 0, $"Repository method {methodName} was not found.");
            var end = repositorySource.IndexOf("\n    public ", start + methodName.Length, StringComparison.Ordinal);
            return repositorySource[start..(end > start ? end : repositorySource.Length)];
        }

        foreach (var methodName in new[]
        {
            "TryAcknowledgeSubmissionAsync",
            "TryTransitionAsync",
            "TryAdvanceCursorAsync",
            "TryCompleteResultsAsync",
            "TryFailIncompleteResultsAsync"
        })
        {
            var method = MethodBody(source, methodName);
            Assert.Contains("WHERE tenant_id = @tenantId", method, StringComparison.Ordinal);
            Assert.Contains("AND lease_token = @leaseToken", method, StringComparison.Ordinal);
            Assert.Contains("AND fencing_version = @fencingVersion", method, StringComparison.Ordinal);
            Assert.Contains("AND lease_expires_at_utc > clock_timestamp()", method, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ExtractionLeaseRenewal_ShouldFenceTenantTokenVersionAndUnexpiredLease()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingExtractionJobRepository.cs"));
        var methodStart = source.IndexOf("public async Task<bool> TryRenewLeaseAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("public async Task<int> ExpireDueAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("DbType.PostgreSQL", method, StringComparison.Ordinal);
        Assert.Contains("WHERE tenant_id = @tenantId", method, StringComparison.Ordinal);
        Assert.Contains("AND basic_id = @jobId", method, StringComparison.Ordinal);
        Assert.Contains("AND lease_token = @leaseToken", method, StringComparison.Ordinal);
        Assert.Contains("AND fencing_version = @fencingVersion", method, StringComparison.Ordinal);
        Assert.Contains("AND lease_expires_at_utc > clock_timestamp()", method, StringComparison.Ordinal);
        Assert.Contains("AND status IN (@submittingStatus, @pendingStatus, @runningStatus, @succeededStatus, @fetchingStatus)", method, StringComparison.Ordinal);
        Assert.Contains("return affected == 1", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SET fencing_version =", method, StringComparison.Ordinal);
    }


    [Fact]
    public void ExtractionFinalPages_ShouldAccumulateCountsWithoutReplacingEarlierPages()
    {
        var tree = new DirectoryInfo(AppContext.BaseDirectory);
        while (tree is not null && !File.Exists(Path.Combine(tree.FullName, "backend", "src")))
            tree = tree.Parent;
        Assert.NotNull(tree);
        var source = File.ReadAllText(Path.Combine(tree!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingExtractionJobRepository.cs"));

        var failedStart = source.IndexOf("public async Task<bool> TryFailIncompleteResultsAsync", StringComparison.Ordinal);
        var completeStart = source.IndexOf("public async Task<bool> TryCompleteResultsAsync", failedStart, StringComparison.Ordinal);
        var transitionStart = source.IndexOf("public async Task<bool> TryTransitionAsync", completeStart, StringComparison.Ordinal);
        Assert.True(failedStart >= 0 && completeStart > failedStart && transitionStart > completeStart);

        var failed = source[failedStart..completeStart];
        var complete = source[completeStart..transitionStart];
        Assert.Contains("result_record_count = result_record_count + @persistedRecordCount", failed, StringComparison.Ordinal);
        Assert.Contains("result_record_count = result_record_count + @persistedRecordCount", complete, StringComparison.Ordinal);
        Assert.Contains("result_failure_count = result_failure_count + @pageFailureCount", failed, StringComparison.Ordinal);
        Assert.Contains("result_failure_count + @failureCount = 0", complete, StringComparison.Ordinal);
        Assert.Contains("result_cursor IS NOT DISTINCT FROM @expectedCursor", failed, StringComparison.Ordinal);
        Assert.Contains("result_cursor IS NOT DISTINCT FROM @expectedCursor", complete, StringComparison.Ordinal);
    }

}
