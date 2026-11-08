using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// PostgreSQL-backed tests for the durable extraction claim path. The real framework
/// CurrentTenant is required because the repository reads AsyncLocalCurrentTenantAccessor
/// directly; a hand-written ICurrentTenant fake does not establish that context.
/// </summary>
public sealed class CrmProspectingExtractionPostgresIntegrationTests : IDisposable
{
    private const long TenantId = 98631;
    private const long OtherTenantId = 98632;
    private readonly ICurrentTenant _tenant1 = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly ICurrentTenant _tenant2 = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly SqlSugarScope _db1;
    private readonly SqlSugarScope _db2;
    private readonly SqlSugarScope _dbObserver;
    private readonly CrmProspectingExtractionJobRepository _repository1;
    private readonly CrmProspectingExtractionJobRepository _repository2;

    public static bool PostgresConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING"));

    public CrmProspectingExtractionPostgresIntegrationTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("PostgreSQL integration tests require KIGER_TEST_POSTGRES_CONNECTION_STRING.");

        _db1 = CreateClient(connectionString);
        _db2 = CreateClient(connectionString);
        _dbObserver = CreateClient(connectionString);
        _db1.CodeFirst.InitTables(typeof(SysCrmProspectingExtractionJob));
        _repository1 = CreateRepository(_db1);
        _repository2 = CreateRepository(_db2);
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task ClaimDue_TwoIndependentClients_ExactlyOneWinsAndReturnsFencedLease()
    {
        var jobId = await InsertDueJobAsync(TenantId, "claim-race");
        try
        {
            var results = await RaceAsync(
                () => WithTenant(_tenant1, TenantId, () => _repository1.ClaimDueAsync("pg-worker-a", TimeSpan.FromMinutes(2), 1)),
                () => WithTenant(_tenant2, TenantId, () => _repository2.ClaimDueAsync("pg-worker-b", TimeSpan.FromMinutes(2), 1)));

            Assert.Equal(1, results.Sum(rows => rows.Count));
            var claimed = Assert.Single(results.SelectMany(rows => rows));
            Assert.Equal(jobId, claimed.BasicId);
            Assert.NotEqual(Guid.Empty, claimed.LeaseToken);
            Assert.Equal(1, claimed.FencingVersion);
            Assert.Equal(TenantId, claimed.TenantId);
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task ClaimDue_FromAnotherTenant_CannotClaimOrReadTheJob()
    {
        var jobId = await InsertDueJobAsync(TenantId, "tenant-isolation");
        try
        {
            var wrongTenant = await WithTenant(
                _tenant2,
                OtherTenantId,
                () => _repository2.ClaimDueAsync("wrong-tenant-worker", TimeSpan.FromMinutes(2), 1));
            Assert.Empty(wrongTenant);

            using (_tenant1.Change(TenantId))
            {
                var visible = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                    .Where(row => row.BasicId == jobId)
                    .SingleAsync();
                Assert.Equal(TenantId, visible.TenantId);
                Assert.Null(visible.LeaseToken);
            }
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task ClaimDue_AfterLeaseExpiry_FencesOutThePreviousWorker()
    {
        var jobId = await InsertDueJobAsync(TenantId, "fencing-takeover");
        try
        {
            var first = Assert.Single(await WithTenant(
                _tenant1,
                TenantId,
                () => _repository1.ClaimDueAsync("pg-worker-old", TimeSpan.FromMinutes(2), 1)));

            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingExtractionJob>()
                    .SetColumns(row => row.LeaseExpiresAtUtc == DateTimeOffset.UtcNow.AddMinutes(-1))
                    .Where(row => row.BasicId == jobId)
                    .ExecuteCommandAsync();
            }

            var second = Assert.Single(await WithTenant(
                _tenant2,
                TenantId,
                () => _repository2.ClaimDueAsync("pg-worker-new", TimeSpan.FromMinutes(2), 1)));

            Assert.Equal(first.BasicId, second.BasicId);
            Assert.NotEqual(first.LeaseToken, second.LeaseToken);
            Assert.Equal(first.FencingVersion + 1, second.FencingVersion);

            var oldLeaseRenewed = await WithTenant(
                _tenant1,
                TenantId,
                () => _repository1.TryRenewLeaseAsync(
                    jobId, first.LeaseToken!.Value, first.FencingVersion, TimeSpan.FromMinutes(2)));
            Assert.False(oldLeaseRenewed);

            var staleWorkerTransitioned = await WithTenant(
                _tenant1,
                TenantId,
                () => _repository1.TryTransitionAsync(
                    jobId,
                    CrmProspectingExtractionStatus.Pending,
                    CrmProspectingExtractionStatus.Running,
                    first.LeaseToken!.Value,
                    first.FencingVersion));
            Assert.False(staleWorkerTransitioned);

            var newLeaseRenewed = await WithTenant(
                _tenant2,
                TenantId,
                () => _repository2.TryRenewLeaseAsync(
                    jobId, second.LeaseToken!.Value, second.FencingVersion, TimeSpan.FromMinutes(2)));
            Assert.True(newLeaseRenewed);

            var currentWorkerTransitioned = await WithTenant(
                _tenant2,
                TenantId,
                () => _repository2.TryTransitionAsync(
                    jobId,
                    CrmProspectingExtractionStatus.Pending,
                    CrmProspectingExtractionStatus.Running,
                    second.LeaseToken!.Value,
                    second.FencingVersion));
            Assert.True(currentWorkerTransitioned);
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task AdvanceCursor_RollsBackWithTheResultPageTransaction()
    {
        var jobId = await InsertDueJobAsync(TenantId, "cursor-transaction-rollback");
        var leaseToken = Guid.NewGuid();
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingExtractionJob>()
                    .SetColumns(row => new SysCrmProspectingExtractionJob
                    {
                        Status = CrmProspectingExtractionStatus.FetchingResults.ToString(),
                        LeaseOwner = "pg-transaction-test",
                        LeaseToken = leaseToken,
                        LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(2),
                        FencingVersion = 7
                    })
                    .Where(row => row.BasicId == jobId)
                    .ExecuteCommandAsync();

                _db1.Ado.BeginTran();
                try
                {
                    var advanced = await _repository1.TryAdvanceCursorAsync(
                        jobId, leaseToken, 7, expectedCursor: null, nextCursor: "page-2",
                        persistedRecordCount: 3);
                    Assert.True(advanced);

                    var insideTransaction = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                        .Where(row => row.BasicId == jobId)
                        .SingleAsync();
                    Assert.Equal("page-2", insideTransaction.ResultCursor);
                    Assert.Equal(3, insideTransaction.ResultRecordCount);

                    _db1.Ado.RollbackTran();
                }
                catch
                {
                    _db1.Ado.RollbackTran();
                    throw;
                }

                var afterRollback = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                    .Where(row => row.BasicId == jobId)
                    .SingleAsync();
                Assert.Null(afterRollback.ResultCursor);
                Assert.Equal(0, afterRollback.ResultRecordCount);
            }
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }


    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task CreateSubmissionIntent_RepeatedRequest_ReusesStableIdempotencyKey()
    {
        const string fingerprint = "A4B8D3E2C1F0A9876543210FEDCBA987A4B8D3E2C1F0A9876543210FEDCBA987";
        long firstJobId = 0;
        try
        {
            SysCrmProspectingExtractionJob first;
            using (_tenant1.Change(TenantId))
            {
                first = await _repository1.CreateSubmissionIntentAsync(
                    SnowFlakeSingle.Instance.NextId(),
                    "postgres-test",
                    fingerprint,
                    DateTimeOffset.UtcNow);
                firstJobId = first.BasicId;
            }

            using (_tenant2.Change(TenantId))
            {
                var second = await _repository2.CreateSubmissionIntentAsync(
                    first.RunId,
                    "POSTGRES-TEST",
                    fingerprint.ToLowerInvariant(),
                    DateTimeOffset.UtcNow.AddMinutes(1));

                Assert.Equal(first.BasicId, second.BasicId);
                Assert.Equal(first.SubmissionIdempotencyKey, second.SubmissionIdempotencyKey);
                Assert.Equal(first.ProviderCode, second.ProviderCode);
                Assert.Equal(first.RequestFingerprint, second.RequestFingerprint);
                Assert.Equal(first.NextPollAtUtc, second.NextPollAtUtc);
                Assert.Equal(CrmProspectingExtractionStatus.Submitting.ToString(), second.Status);
            }

        }
        finally
        {
            if (firstJobId > 0)
                await DeleteJobAsync(firstJobId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task CreateSubmissionIntent_ConcurrentFirstRequests_UsesUniqueIndexAsFinalArbiter()
    {
        const string fingerprint = "B5C9E4F3D2A109876543210FEDCBA987B5C9E4F3D2A109876543210FEDCBA987";
        var runId = SnowFlakeSingle.Instance.NextId();
        long persistedJobId = 0;

        async Task<(SysCrmProspectingExtractionJob? Job, Exception? Error)> CaptureAsync(
            ICurrentTenant tenant,
            CrmProspectingExtractionJobRepository repository)
        {
            try
            {
                var job = await WithTenant(tenant, TenantId, () =>
                    repository.CreateSubmissionIntentAsync(
                        runId, "postgres-race", fingerprint, DateTimeOffset.UtcNow));
                return (job, null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }

        try
        {
            // Two independent clients may both observe no row. The database unique index,
            // not an in-process lock, must arbitrate the first insert.
            var outcomes = await Task.WhenAll(
                CaptureAsync(_tenant1, _repository1),
                CaptureAsync(_tenant2, _repository2));

            var winner = Assert.Single(outcomes.Where(outcome => outcome.Job is not null)).Job!;
            var loser = Assert.Single(outcomes.Where(outcome => outcome.Error is not null));
            persistedJobId = winner.BasicId;

            Assert.NotNull(loser.Error);

            // The failed PostgreSQL transaction is not reused for reconciliation.
            // A fresh operation on the other client must now read the committed winner.
            var replay = await WithTenant(_tenant2, TenantId, () =>
                _repository2.CreateSubmissionIntentAsync(
                    runId, "POSTGRES-RACE", fingerprint.ToLowerInvariant(),
                    DateTimeOffset.UtcNow.AddMinutes(1)));

            Assert.Equal(winner.BasicId, replay.BasicId);
            Assert.Equal(winner.SubmissionIdempotencyKey, replay.SubmissionIdempotencyKey);
            Assert.Equal(winner.ProviderCode, replay.ProviderCode);
            Assert.Equal(winner.RequestFingerprint, replay.RequestFingerprint);
            Assert.Equal(winner.NextPollAtUtc, replay.NextPollAtUtc);
            Assert.Equal(CrmProspectingExtractionStatus.Submitting.ToString(), replay.Status);
        }
        finally
        {
            if (persistedJobId > 0)
                await DeleteJobAsync(persistedJobId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task ExpireDue_ExpiredProviderJob_ReleasesLeaseAndCannotBeClaimed()
    {
        var jobId = await InsertDueJobAsync(TenantId, "expired-provider-job");
        var leaseToken = Guid.NewGuid();
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingExtractionJob>()
                    .SetColumns(row => new SysCrmProspectingExtractionJob
                    {
                        Status = CrmProspectingExtractionStatus.Running.ToString(),
                        ProviderExtractionId = "provider-expired-job",
                        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                        LeaseOwner = "expired-worker",
                        LeaseToken = leaseToken,
                        LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(2),
                        FencingVersion = 9
                    })
                    .Where(row => row.BasicId == jobId)
                    .ExecuteCommandAsync();

                var expiredCount = await _repository1.ExpireDueAsync();
                Assert.True(expiredCount >= 1);

                var persisted = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                    .Where(row => row.BasicId == jobId)
                    .SingleAsync();
                Assert.Equal(CrmProspectingExtractionStatus.Expired.ToString(), persisted.Status);
                Assert.Equal("provider_extraction_expired", persisted.FailureCode);
                Assert.Null(persisted.LeaseOwner);
                Assert.Null(persisted.LeaseToken);
                Assert.Null(persisted.LeaseExpiresAtUtc);

                var claimable = await _repository1.ClaimDueAsync(
                    "replacement-worker", TimeSpan.FromMinutes(2), 10);
                Assert.DoesNotContain(claimable, row => row.BasicId == jobId);
            }
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }


    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task AcknowledgeSubmission_ExpiredLease_CannotPersistRemoteAcknowledgement()
    {
        var jobId = await InsertDueJobAsync(TenantId, "expired-submission-ack");
        var leaseToken = Guid.NewGuid();
        var submittedAt = DateTimeOffset.UtcNow;
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingExtractionJob>()
                    .SetColumns(row => new SysCrmProspectingExtractionJob
                    {
                        Status = CrmProspectingExtractionStatus.Submitting.ToString(),
                        LeaseOwner = "expired-submit-worker",
                        LeaseToken = leaseToken,
                        LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                        FencingVersion = 11
                    })
                    .Where(row => row.BasicId == jobId)
                    .ExecuteCommandAsync();

                var acknowledged = await _repository1.TryAcknowledgeSubmissionAsync(
                    jobId,
                    leaseToken,
                    fencingVersion: 11,
                    providerExtractionId: "provider-accepted-after-lease-expiry",
                    submittedAtUtc: submittedAt,
                    nextPollAtUtc: submittedAt.AddMinutes(1),
                    expiresAtUtc: submittedAt.AddHours(1));

                Assert.False(acknowledged);

                var persisted = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                    .Where(row => row.BasicId == jobId)
                    .SingleAsync();
                Assert.Equal(CrmProspectingExtractionStatus.Submitting.ToString(), persisted.Status);
                Assert.Null(persisted.ProviderExtractionId);
                Assert.Equal(leaseToken, persisted.LeaseToken);
                Assert.Equal(11, persisted.FencingVersion);
            }
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }


    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task RenewLease_ExpiredLease_CannotBeResurrected()
    {
        var jobId = await InsertDueJobAsync(TenantId, "expired-lease-renewal");
        var leaseToken = Guid.NewGuid();
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingExtractionJob>()
                    .SetColumns(row => new SysCrmProspectingExtractionJob
                    {
                        Status = CrmProspectingExtractionStatus.Running.ToString(),
                        LeaseOwner = "expired-renewal-worker",
                        LeaseToken = leaseToken,
                        LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                        FencingVersion = 17
                    })
                    .Where(row => row.BasicId == jobId)
                    .ExecuteCommandAsync();

                var renewed = await _repository1.TryRenewLeaseAsync(
                    jobId,
                    leaseToken,
                    fencingVersion: 17,
                    leaseDuration: TimeSpan.FromMinutes(2));

                Assert.False(renewed);

                var persisted = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                    .Where(row => row.BasicId == jobId)
                    .SingleAsync();
                Assert.Equal(leaseToken, persisted.LeaseToken);
                Assert.Equal(17, persisted.FencingVersion);
                Assert.True(persisted.LeaseExpiresAtUtc <= DateTimeOffset.UtcNow);
            }
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }


    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task Transition_ExpiredLease_CannotChangeExtractionLifecycle()
    {
        var jobId = await InsertDueJobAsync(TenantId, "expired-transition");
        var leaseToken = Guid.NewGuid();
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingExtractionJob>()
                    .SetColumns(row => new SysCrmProspectingExtractionJob
                    {
                        Status = CrmProspectingExtractionStatus.Running.ToString(),
                        ProviderExtractionId = "remote-expired-transition",
                        LeaseOwner = "expired-transition-worker",
                        LeaseToken = leaseToken,
                        LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                        FencingVersion = 19
                    })
                    .Where(row => row.BasicId == jobId)
                    .ExecuteCommandAsync();

                var transitioned = await _repository1.TryTransitionAsync(
                    jobId,
                    CrmProspectingExtractionStatus.Running,
                    CrmProspectingExtractionStatus.NeedsReview,
                    leaseToken,
                    fencingVersion: 19,
                    safeFailureSummary: "stale worker must not transition",
                    cancellationToken: CancellationToken.None);

                Assert.False(transitioned);

                var persisted = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                    .Where(row => row.BasicId == jobId)
                    .SingleAsync();
                Assert.Equal(CrmProspectingExtractionStatus.Running.ToString(), persisted.Status);
                Assert.Equal(leaseToken, persisted.LeaseToken);
                Assert.Equal(19, persisted.FencingVersion);
                Assert.True(persisted.LeaseExpiresAtUtc <= DateTimeOffset.UtcNow);
            }
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task CompleteResults_ExpiredLease_CannotMarkExtractionSucceeded()
    {
        var jobId = await InsertDueJobAsync(TenantId, "expired-final-page-commit");
        var leaseToken = Guid.NewGuid();
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingExtractionJob>()
                    .SetColumns(row => new SysCrmProspectingExtractionJob
                    {
                        Status = CrmProspectingExtractionStatus.FetchingResults.ToString(),
                        LeaseOwner = "expired-final-page-worker",
                        LeaseToken = leaseToken,
                        LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                        FencingVersion = 23
                    })
                    .Where(row => row.BasicId == jobId)
                    .ExecuteCommandAsync();

                var completed = await _repository1.TryCompleteResultsAsync(
                    jobId,
                    leaseToken,
                    fencingVersion: 23,
                    isLastPage: true,
                    isComplete: true,
                    failureCount: 0,
                    persistedRecordCount: 4,
                    expectedCursor: null);

                Assert.False(completed);

                var persisted = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                    .Where(row => row.BasicId == jobId)
                    .SingleAsync();
                Assert.Equal(CrmProspectingExtractionStatus.FetchingResults.ToString(), persisted.Status);
                Assert.Equal(0, persisted.ResultRecordCount);
                Assert.Equal(leaseToken, persisted.LeaseToken);
                Assert.Equal(23, persisted.FencingVersion);
            }
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task FailIncompleteResults_ExpiredLease_CannotCommitPartialPageCounts()
    {
        var jobId = await InsertDueJobAsync(TenantId, "expired-incomplete-final-page");
        var leaseToken = Guid.NewGuid();
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingExtractionJob>()
                    .SetColumns(row => new SysCrmProspectingExtractionJob
                    {
                        Status = CrmProspectingExtractionStatus.FetchingResults.ToString(),
                        LeaseOwner = "expired-partial-page-worker",
                        LeaseToken = leaseToken,
                        LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                        FencingVersion = 29,
                        ResultRecordCount = 3,
                        ResultFailureCount = 1
                    })
                    .Where(row => row.BasicId == jobId)
                    .ExecuteCommandAsync();

                var failed = await _repository1.TryFailIncompleteResultsAsync(
                    jobId,
                    leaseToken,
                    fencingVersion: 29,
                    isLastPage: true,
                    isComplete: true,
                    pageFailureCount: 2,
                    persistedRecordCount: 4,
                    expectedCursor: null);

                Assert.False(failed);

                var persisted = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                    .Where(row => row.BasicId == jobId)
                    .SingleAsync();
                Assert.Equal(CrmProspectingExtractionStatus.FetchingResults.ToString(), persisted.Status);
                Assert.Equal(3, persisted.ResultRecordCount);
                Assert.Equal(1, persisted.ResultFailureCount);
                Assert.Equal(leaseToken, persisted.LeaseToken);
                Assert.Equal(29, persisted.FencingVersion);
            }
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }

    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task AdvanceCursor_ExpiredLease_CannotAdvanceCursorOrAccumulatePageCounts()
    {
        var jobId = await InsertDueJobAsync(TenantId, "expired-cursor-advance");
        var leaseToken = Guid.NewGuid();
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingExtractionJob>()
                    .SetColumns(row => new SysCrmProspectingExtractionJob
                    {
                        Status = CrmProspectingExtractionStatus.FetchingResults.ToString(),
                        LeaseOwner = "expired-cursor-worker",
                        LeaseToken = leaseToken,
                        LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                        FencingVersion = 31,
                        ResultCursor = "cursor-before"
                    })
                    .Where(row => row.BasicId == jobId)
                    .ExecuteCommandAsync();

                _db1.Ado.BeginTran();
                try
                {
                    var advanced = await _repository1.TryAdvanceCursorAsync(
                        jobId,
                        leaseToken,
                        fencingVersion: 31,
                        expectedCursor: "cursor-before",
                        nextCursor: "cursor-after",
                        persistedRecordCount: 5,
                        pageFailureCount: 0);

                    Assert.False(advanced);
                    await _db1.Ado.RollbackTranAsync();
                }
                catch
                {
                    await _db1.Ado.RollbackTranAsync();
                    throw;
                }

                var persisted = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                    .Where(row => row.BasicId == jobId)
                    .SingleAsync();
                Assert.Equal(CrmProspectingExtractionStatus.FetchingResults.ToString(), persisted.Status);
                Assert.Equal("cursor-before", persisted.ResultCursor);
                Assert.Equal(0, persisted.ResultRecordCount);
                Assert.Equal(0, persisted.ResultFailureCount);
                Assert.Equal(leaseToken, persisted.LeaseToken);
                Assert.Equal(31, persisted.FencingVersion);
            }
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }


    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task ClaimDue_FuturePollTime_DoesNotAcquireLease()
    {
        var jobId = await InsertDueJobAsync(TenantId, "future-poll-not-due");
        var futurePollAt = DateTimeOffset.UtcNow.AddMinutes(10);
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingExtractionJob>()
                    .SetColumns(row => row.NextPollAtUtc == futurePollAt)
                    .Where(row => row.BasicId == jobId)
                    .ExecuteCommandAsync();
            }

            var claimed = await WithTenant(
                _tenant2,
                TenantId,
                () => _repository2.ClaimDueAsync("future-poll-worker", TimeSpan.FromMinutes(2), 10));

            Assert.DoesNotContain(claimed, row => row.BasicId == jobId);
            using (_tenant1.Change(TenantId))
            {
                var persisted = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                    .Where(row => row.BasicId == jobId)
                    .SingleAsync();
                Assert.Null(persisted.LeaseToken);
                Assert.Null(persisted.LeaseOwner);
                Assert.Null(persisted.LeaseExpiresAtUtc);
                Assert.Equal(futurePollAt, persisted.NextPollAtUtc);
            }
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }


    [Fact(SkipUnless = nameof(PostgresConfigured))]
    public async Task AdvanceCursor_TwoIndependentTransactions_OnlyOneCanCommitExpectedCursor()
    {
        var jobId = await InsertDueJobAsync(TenantId, "cursor-cas-race");
        var leaseToken = Guid.NewGuid();
        try
        {
            using (_tenant1.Change(TenantId))
            {
                await _db1.Updateable<SysCrmProspectingExtractionJob>()
                    .SetColumns(row => new SysCrmProspectingExtractionJob
                    {
                        Status = CrmProspectingExtractionStatus.FetchingResults.ToString(),
                        LeaseOwner = "cursor-race-worker",
                        LeaseToken = leaseToken,
                        LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(2),
                        FencingVersion = 23
                    })
                    .Where(row => row.BasicId == jobId)
                    .ExecuteCommandAsync();
            }

            Task<bool>? secondAttempt = null;
            var secondAttemptStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (_tenant1.Change(TenantId))
            {
                _db1.Ado.BeginTran();
                try
                {
                    var firstApplied = await _repository1.TryAdvanceCursorAsync(
                        jobId, leaseToken, 23, expectedCursor: null, nextCursor: "page-2",
                        persistedRecordCount: 4);
                    Assert.True(firstApplied);

                    secondAttempt = Task.Run(async () =>
                    {
                        using (_tenant2.Change(TenantId))
                        {
                            _db2.Ado.BeginTran();
                            try
                            {
                                secondAttemptStarted.TrySetResult();
                                var advanced = await _repository2.TryAdvanceCursorAsync(
                                    jobId, leaseToken, 23, expectedCursor: null, nextCursor: "other-page-2",
                                    persistedRecordCount: 9);
                                if (advanced) _db2.Ado.CommitTran();
                                else _db2.Ado.RollbackTran();
                                return advanced;
                            }
                            catch
                            {
                                _db2.Ado.RollbackTran();
                                throw;
                            }
                        }
                    });

                    // Do not guess with a fixed delay: observe PostgreSQL itself reporting that
                    // the competing UPDATE is blocked on a lock before releasing the first writer.
                    await secondAttemptStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await WaitForCursorUpdateLockWaitAsync(TimeSpan.FromSeconds(5));
                    _db1.Ado.CommitTran();
                }
                catch
                {
                    _db1.Ado.RollbackTran();
                    throw;
                }
            }

            Assert.NotNull(secondAttempt);
            Assert.False(await secondAttempt);

            using (_tenant1.Change(TenantId))
            {
                var persisted = await _db1.Queryable<SysCrmProspectingExtractionJob>()
                    .Where(row => row.BasicId == jobId)
                    .SingleAsync();
                Assert.Equal("page-2", persisted.ResultCursor);
                Assert.Equal(4, persisted.ResultRecordCount);
            }
        }
        finally
        {
            await DeleteJobAsync(jobId);
        }
    }


    private async Task WaitForCursorUpdateLockWaitAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var waiting = await _dbObserver.Ado.SqlQuery<int>(
                "SELECT COUNT(*) FROM pg_stat_activity " +
                "WHERE wait_event_type = 'Lock' AND state = 'active' " +
                "AND query ILIKE '%SysCrmProspectingExtractionJob%'");
            if (waiting.Count > 0 && waiting[0] > 0) return;
            await Task.Delay(20);
        }

        throw new TimeoutException(
            "PostgreSQL did not report the competing extraction-job UPDATE waiting on a row lock.");
    }

    private async Task<long> InsertDueJobAsync(long tenantId, string identity)
    {
        var jobId = SnowFlakeSingle.Instance.NextId();
        using (_tenant1.Change(tenantId))
        {
            await _db1.Insertable(new SysCrmProspectingExtractionJob
            {
                BasicId = jobId,
                TenantId = tenantId,
                RunId = SnowFlakeSingle.Instance.NextId(),
                ProviderCode = "postgres-test",
                RequestFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(identity))),
                SubmissionIdempotencyKey = Guid.NewGuid(),
                Status = CrmProspectingExtractionStatus.Pending.ToString(),
                NextPollAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                PollAttemptCount = 0,
                ResultRecordCount = 0,
                ResultFailureCount = 0,
                FencingVersion = 0
            }).ExecuteCommandAsync();
        }

        return jobId;
    }

    private static async Task<IReadOnlyList<SysCrmProspectingExtractionJob>[]> RaceAsync(
        Func<Task<IReadOnlyList<SysCrmProspectingExtractionJob>>> first,
        Func<Task<IReadOnlyList<SysCrmProspectingExtractionJob>>> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = Task.Run(async () => { await start.Task; return await first(); });
        var secondTask = Task.Run(async () => { await start.Task; return await second(); });
        start.SetResult();
        return await Task.WhenAll(firstTask, secondTask);
    }

    private async Task DeleteJobAsync(long jobId)
    {
        using (_tenant1.Change(TenantId))
        {
            await _db1.Deleteable<SysCrmProspectingExtractionJob>()
                .Where(row => row.BasicId == jobId)
                .ExecuteCommandAsync();
        }
    }

    private static async Task<T> WithTenant<T>(ICurrentTenant tenant, long tenantId, Func<Task<T>> action)
    {
        using (tenant.Change(tenantId))
            return await action();
    }

    private static SqlSugarScope CreateClient(string connectionString)
        => new(new ConnectionConfig
        {
            DbType = DbType.PostgreSQL,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        }, db =>
        {
            db.QueryFilter.AddTableFilter<ISoftDelete>(entity => !entity.IsDeleted);
            db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(
                entity => ScopeTenantId() == entity.TenantId);
            db.Aop.DataExecuting = (_, entityInfo) =>
            {
                if (entityInfo.OperationType == DataFilterType.InsertByObject)
                {
                    entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                    entityInfo.ToCreated(EntityAuditContext.From(null, ScopeTenantId()));
                }
            };
        });

    private static long ScopeTenantId()
        => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;

    private static CrmProspectingExtractionJobRepository CreateRepository(ISqlSugarClient client)
    {
        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(client);
        return new CrmProspectingExtractionJobRepository(resolver.Object);
    }

    public void Dispose()
    {
        _db1.Dispose();
        _db2.Dispose();
        _dbObserver.Dispose();
    }
}
