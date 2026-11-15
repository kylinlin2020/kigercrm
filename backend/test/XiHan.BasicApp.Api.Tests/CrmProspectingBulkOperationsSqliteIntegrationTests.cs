// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Caching.Distributed.Abstracts;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Uow;
using XiHan.Framework.Uow.Options;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Exercises the real <see cref="CrmProspectingAppService"/> bulk operations against SQLite with
/// the production repository stack: honest per-item outcomes (success / idempotent skip / failure
/// with reason), partial-failure states, the candidate state machine, tenant isolation, caller
/// data scope, shortlist semantics and cancellation transparency. The unit-of-work manager is a
/// no-op mock: per-item transaction enlistment is the framework's SqlSugarClientResolver path,
/// while this suite pins the orchestration semantics around it.
/// </summary>
public sealed class CrmProspectingBulkOperationsSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 9921;
    private const long OtherTenantId = 9922;
    private const long OwnerUserId = 88;
    private const long OutOfScopeUserId = 999;
    private const long MissingCandidateId = 424242424242424;

    private readonly string _connectionString = $"Data Source=kiger-crm-bulk-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    // Repository write guards read AsyncLocalCurrentTenantAccessor.Instance.Current;
    // use the framework CurrentTenant so filters, AOP auditing and the guard agree.
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly Mock<IUserDataScopeFilterService> _dataScope = new();
    private readonly Mock<IFieldSecurityService> _fieldSecurity = new();
    private readonly Mock<IRedisDelayQueue<CrmProspectingRunMessage>> _runQueue = new();
    private readonly Mock<IUnitOfWorkManager> _unitOfWorkManager = new();
    private readonly Mock<IConfiguration> _configuration = new();
    private readonly CrmProspectingCampaignRepository _campaigns;
    private readonly CrmProspectingStrategyRepository _strategies;
    private readonly CrmProspectingRunRepository _runs;
    private readonly CrmProspectingCandidateRepository _candidates;
    private readonly CrmLeadRepository _leads;
    private readonly CrmProspectingAppService _service;
    private UserDataScopeFilter _scope = UserDataScopeFilter.Unlimited;

    public CrmProspectingBulkOperationsSqliteIntegrationTests()
    {
        _tenantScope = _tenant.Change(TenantId);
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        var tenant = _tenant;
        _db = new SqlSugarScope(
            new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = _connectionString,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            },
            db =>
            {
                db.QueryFilter.AddTableFilter<ISoftDelete>(entity => !entity.IsDeleted);
                db.QueryFilter.AddTableFilter<IMultiTenantEntity>(entity => entity.TenantId == 0 || ScopeId() == entity.TenantId);
                db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(entity => ScopeId() == entity.TenantId);
                db.Aop.DataExecuting = (value, entityInfo) =>
                {
                    if (entityInfo.OperationType == DataFilterType.InsertByObject)
                    {
                        entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                        entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                    }
                };
            });

        _db.CodeFirst.InitTables(
            typeof(SysCrmProspectingCampaign),
            typeof(SysCrmProspectingRun),
            typeof(SysCrmProspectingCandidate),
            typeof(SysCrmLead));

        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _campaigns = new CrmProspectingCampaignRepository(_resolver.Object);
        _strategies = new CrmProspectingStrategyRepository(_resolver.Object);
        _runs = new CrmProspectingRunRepository(_resolver.Object);
        _candidates = new CrmProspectingCandidateRepository(_resolver.Object);
        var evidence = new CrmProspectingEvidenceRepository(_resolver.Object);
        var scores = new CrmProspectingScoreRepository(_resolver.Object);
        _leads = new CrmLeadRepository(_resolver.Object);
        var customers = new CrmCustomerRepository(_resolver.Object);
        var contacts = new CrmContactRepository(_resolver.Object);
        var extractionJobs = new CrmProspectingExtractionJobRepository(_resolver.Object);

        _dataScope
            .Setup(value => value.ResolveAccessibleUsersAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(_scope));
        // Field-level security is a no-op for these fixtures: Moq returns completed tasks for
        // unconfigured Task members. Ensure*Async<TInput> is constrained to class/new(), so
        // It.IsAnyType cannot be used as its type argument (CS1955/CS0311).
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(value => value.CompleteAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _unitOfWorkManager
            .Setup(value => value.Begin(It.IsAny<XiHanUnitOfWorkOptions>(), It.IsAny<bool>()))
            .Returns(unitOfWork.Object);

        _service = new CrmProspectingAppService(
            new CrmProspectingDomainService(_campaigns, _strategies, _runs),
            _campaigns,
            _strategies,
            _runs,
            _candidates,
            evidence,
            scores,
            _leads,
            new CrmLeadDomainService(_leads, customers, contacts),
            _fieldSecurity.Object,
            _dataScope.Object,
            _runQueue.Object,
            _unitOfWorkManager.Object,
            extractionJobs,
            CrmProspectingProviderRegistry.Create([], []),
            _configuration.Object);
    }

    [Fact]
    public async Task BulkReview_ReportsPerItemOutcomes_AndKeepsFailedItemStateUntouched()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync();
            var pending1 = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "p1");
            var pending2 = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "p2");
            var pending3 = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "p3");
            var rejected = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.Rejected, "r1");

            var result = await _service.BulkReviewCandidatesAsync(new CrmProspectingCandidateBulkReviewDto
            {
                CandidateIds = [pending1, pending2, pending3, rejected],
                Approve = true
            });

            // 3 successes and exactly 1 failure, reported in the caller's id order.
            Assert.Equal(4, result.Items.Count);
            Assert.Equal(new[] { pending1, pending2, pending3, rejected }, result.Items.Select(item => item.CandidateId));
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)result.Items[0].Outcome);
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)result.Items[1].Outcome);
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)result.Items[2].Outcome);
            Assert.Equal(CrmProspectingBulkOutcome.Failed, (CrmProspectingBulkOutcome)result.Items[3].Outcome);
            Assert.Contains("Only pending or duplicate candidates can be reviewed", result.Items[3].Reason, StringComparison.Ordinal);
            Assert.Null(result.Items[3].LeadId);

            // The committed states match the reported outcomes one by one.
            Assert.Equal(CrmProspectingCandidateStatus.Approved, (await GetStoredAsync(pending1)).Status);
            Assert.Equal(CrmProspectingCandidateStatus.Approved, (await GetStoredAsync(pending2)).Status);
            Assert.Equal(CrmProspectingCandidateStatus.Approved, (await GetStoredAsync(pending3)).Status);
            Assert.Equal(CrmProspectingCandidateStatus.Rejected, (await GetStoredAsync(rejected)).Status);
        }
    }

    [Fact]
    public async Task BulkReview_EnforcesTheStateMachineAndExplicitDuplicateOverride()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync();
            var pending = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "l1");
            var duplicate = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.Duplicate, "l2");
            var approved = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.Approved, "l3");
            var rejected = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.Rejected, "l4");
            var converted = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.Converted, "l5");
            await SetConvertedLeadAsync(converted, await SeedLeadAsync());

            // Duplicate approval without the explicit override fails and changes nothing.
            var noOverride = await _service.BulkReviewCandidatesAsync(new CrmProspectingCandidateBulkReviewDto
            {
                CandidateIds = [duplicate],
                Approve = true
            });
            Assert.Equal(CrmProspectingBulkOutcome.Failed, (CrmProspectingBulkOutcome)noOverride.Items[0].Outcome);
            Assert.Contains("explicit duplicate override", noOverride.Items[0].Reason, StringComparison.Ordinal);
            Assert.Equal(CrmProspectingCandidateStatus.Duplicate, (await GetStoredAsync(duplicate)).Status);

            // With the override the duplicate moves Duplicate -> Approved.
            var withOverride = await _service.BulkReviewCandidatesAsync(new CrmProspectingCandidateBulkReviewDto
            {
                CandidateIds = [duplicate],
                Approve = true,
                OverrideDuplicate = true
            });
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)withOverride.Items[0].Outcome);
            Assert.Equal(CrmProspectingCandidateStatus.Approved, (await GetStoredAsync(duplicate)).Status);

            // Illegal transitions (approve an Approved, a Converted and a Rejected row) all fail
            // with the state-machine reason and leave every status untouched.
            var illegal = await _service.BulkReviewCandidatesAsync(new CrmProspectingCandidateBulkReviewDto
            {
                CandidateIds = [approved, converted, rejected],
                Approve = true
            });
            Assert.Equal(3, illegal.Items.Count);
            Assert.All(illegal.Items, item =>
            {
                Assert.Equal(CrmProspectingBulkOutcome.Failed, (CrmProspectingBulkOutcome)item.Outcome);
                Assert.Contains("Only pending or duplicate candidates can be reviewed", item.Reason, StringComparison.Ordinal);
            });
            Assert.Equal(CrmProspectingCandidateStatus.Approved, (await GetStoredAsync(approved)).Status);
            Assert.Equal(CrmProspectingCandidateStatus.Converted, (await GetStoredAsync(converted)).Status);
            Assert.Equal(CrmProspectingCandidateStatus.Rejected, (await GetStoredAsync(rejected)).Status);

            // PendingReview -> Rejected through the same batch surface.
            var reject = await _service.BulkReviewCandidatesAsync(new CrmProspectingCandidateBulkReviewDto
            {
                CandidateIds = [pending],
                Approve = false
            });
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)reject.Items[0].Outcome);
            Assert.Equal(CrmProspectingCandidateStatus.Rejected, (await GetStoredAsync(pending)).Status);
        }
    }

    [Fact]
    public async Task BulkConvert_ConvertsApprovedItems_AndIdempotentReplayNeverDuplicatesLeads()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync();
            var approved1 = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.Approved, "a1");
            var approved2 = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.Approved, "a2");
            var pending = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "w1");
            var seededLeadId = await SeedLeadAsync();
            var converted = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.Converted, "c1");
            await SetConvertedLeadAsync(converted, seededLeadId);

            var first = await _service.BulkConvertCandidatesAsync(new CrmProspectingCandidateBulkDto
            {
                CandidateIds = [approved1, approved2, pending, converted]
            });

            Assert.Equal(4, first.Items.Count);
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)first.Items[0].Outcome);
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)first.Items[1].Outcome);
            Assert.NotNull(first.Items[0].LeadId);
            Assert.NotNull(first.Items[1].LeadId);
            Assert.NotEqual(seededLeadId, first.Items[0].LeadId);
            Assert.NotEqual(seededLeadId, first.Items[1].LeadId);

            // A non-approved candidate fails with the conversion state-machine reason.
            Assert.Equal(CrmProspectingBulkOutcome.Failed, (CrmProspectingBulkOutcome)first.Items[2].Outcome);
            Assert.Contains("Only an approved prospecting candidate can be converted", first.Items[2].Reason, StringComparison.Ordinal);

            // The already-converted candidate is skipped and reports its existing Lead.
            Assert.Equal(CrmProspectingBulkOutcome.Skipped, (CrmProspectingBulkOutcome)first.Items[3].Outcome);
            Assert.Contains("already converted to a Lead", first.Items[3].Reason, StringComparison.Ordinal);
            Assert.Equal(seededLeadId, first.Items[3].LeadId);

            var stored1 = await GetStoredAsync(approved1);
            Assert.Equal(CrmProspectingCandidateStatus.Converted, stored1.Status);
            Assert.Equal(first.Items[0].LeadId, stored1.ConvertedLeadId);
            var stored2 = await GetStoredAsync(approved2);
            Assert.Equal(CrmProspectingCandidateStatus.Converted, stored2.Status);
            Assert.Equal(first.Items[1].LeadId, stored2.ConvertedLeadId);
            Assert.Equal(CrmProspectingCandidateStatus.PendingReview, (await GetStoredAsync(pending)).Status);
            Assert.Equal(seededLeadId, (await GetStoredAsync(converted)).ConvertedLeadId);

            // The seeded lead plus the two fresh conversions: exactly three leads exist.
            Assert.Equal(3, await CountLeadsAsync());

            // Idempotent replay: every previously converted item is skipped with the same Lead id,
            // the pending item still fails, and no fourth Lead is ever created.
            var second = await _service.BulkConvertCandidatesAsync(new CrmProspectingCandidateBulkDto
            {
                CandidateIds = [approved1, approved2, pending, converted]
            });
            Assert.Equal(CrmProspectingBulkOutcome.Skipped, (CrmProspectingBulkOutcome)second.Items[0].Outcome);
            Assert.Equal(first.Items[0].LeadId, second.Items[0].LeadId);
            Assert.Equal(CrmProspectingBulkOutcome.Skipped, (CrmProspectingBulkOutcome)second.Items[1].Outcome);
            Assert.Equal(first.Items[1].LeadId, second.Items[1].LeadId);
            Assert.Equal(CrmProspectingBulkOutcome.Failed, (CrmProspectingBulkOutcome)second.Items[2].Outcome);
            Assert.Equal(CrmProspectingBulkOutcome.Skipped, (CrmProspectingBulkOutcome)second.Items[3].Outcome);
            Assert.Equal(seededLeadId, second.Items[3].LeadId);
            Assert.Equal(3, await CountLeadsAsync());
        }
    }

    [Fact]
    public async Task BulkOperations_CrossTenantCandidatesFailAsMissing_AndNeverTouchForeignState()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync();
            var foreign = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "x1", OtherTenantId);
            var mine = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "m1");

            var review = await _service.BulkReviewCandidatesAsync(new CrmProspectingCandidateBulkReviewDto
            {
                CandidateIds = [foreign, mine],
                Approve = true
            });
            Assert.Equal(CrmProspectingBulkOutcome.Failed, (CrmProspectingBulkOutcome)review.Items[0].Outcome);
            Assert.Contains("does not exist", review.Items[0].Reason, StringComparison.Ordinal);
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)review.Items[1].Outcome);

            var convert = await _service.BulkConvertCandidatesAsync(new CrmProspectingCandidateBulkDto
            {
                CandidateIds = [foreign]
            });
            Assert.Equal(CrmProspectingBulkOutcome.Failed, (CrmProspectingBulkOutcome)convert.Items[0].Outcome);
            Assert.Contains("does not exist", convert.Items[0].Reason, StringComparison.Ordinal);

            var shortlist = await _service.ShortlistCandidatesAsync(new CrmProspectingCandidateBulkDto
            {
                CandidateIds = [foreign]
            });
            Assert.Equal(CrmProspectingBulkOutcome.Failed, (CrmProspectingBulkOutcome)shortlist.Items[0].Outcome);
            Assert.Contains("does not exist", shortlist.Items[0].Reason, StringComparison.Ordinal);

            // The foreign row is observed past the tenant filter and must be fully untouched.
            var storedForeign = await GetStoredAsync(foreign);
            Assert.Equal(CrmProspectingCandidateStatus.PendingReview, storedForeign.Status);
            Assert.Null(storedForeign.ShortlistedAt);
            Assert.Equal(0, await CountLeadsAsync());
        }
    }

    [Fact]
    public async Task BulkOperations_OutOfScopeCallerFailsEveryItemWithoutStateChanges()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync();
            var first = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "s1");
            var second = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "s2");
            _scope = new UserDataScopeFilter(false, [OutOfScopeUserId]);

            var review = await _service.BulkReviewCandidatesAsync(new CrmProspectingCandidateBulkReviewDto
            {
                CandidateIds = [first, second],
                Approve = true
            });
            Assert.Equal(2, review.Items.Count);
            Assert.All(review.Items, item =>
            {
                Assert.Equal(CrmProspectingBulkOutcome.Failed, (CrmProspectingBulkOutcome)item.Outcome);
                Assert.Contains("outside the caller's allowed data scope", item.Reason, StringComparison.Ordinal);
            });
            Assert.Equal(CrmProspectingCandidateStatus.PendingReview, (await GetStoredAsync(first)).Status);

            var shortlist = await _service.ShortlistCandidatesAsync(new CrmProspectingCandidateBulkDto
            {
                CandidateIds = [first]
            });
            Assert.Equal(CrmProspectingBulkOutcome.Failed, (CrmProspectingBulkOutcome)shortlist.Items[0].Outcome);
            Assert.Null((await GetStoredAsync(first)).ShortlistedAt);

            var convert = await _service.BulkConvertCandidatesAsync(new CrmProspectingCandidateBulkDto
            {
                CandidateIds = [first]
            });
            Assert.Equal(CrmProspectingBulkOutcome.Failed, (CrmProspectingBulkOutcome)convert.Items[0].Outcome);
            Assert.Equal(CrmProspectingCandidateStatus.PendingReview, (await GetStoredAsync(first)).Status);
            Assert.Equal(0, await CountLeadsAsync());
        }
    }

    [Fact]
    public async Task ShortlistBatches_AreIdempotentPerItem_AndReportMissingIdsHonestly()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync();
            var first = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "q1");
            var second = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "q2");

            // A missing id fails alone while the surrounding items still commit.
            var added = await _service.ShortlistCandidatesAsync(new CrmProspectingCandidateBulkDto
            {
                CandidateIds = [first, MissingCandidateId, second]
            });
            Assert.Equal(3, added.Items.Count);
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)added.Items[0].Outcome);
            Assert.Equal(CrmProspectingBulkOutcome.Failed, (CrmProspectingBulkOutcome)added.Items[1].Outcome);
            Assert.Contains("does not exist", added.Items[1].Reason, StringComparison.Ordinal);
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)added.Items[2].Outcome);
            Assert.NotNull((await GetStoredAsync(first)).ShortlistedAt);
            Assert.NotNull((await GetStoredAsync(second)).ShortlistedAt);

            // Repeating the request is an idempotent success that does not rewrite the timestamp.
            var firstStamp = (await GetStoredAsync(first)).ShortlistedAt;
            var replay = await _service.ShortlistCandidatesAsync(new CrmProspectingCandidateBulkDto
            {
                CandidateIds = [first]
            });
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)replay.Items[0].Outcome);
            Assert.Equal(firstStamp, (await GetStoredAsync(first)).ShortlistedAt);

            // Removal clears only the marker: review status is untouched and repeating it succeeds.
            var removed = await _service.UnshortlistCandidatesAsync(new CrmProspectingCandidateBulkDto
            {
                CandidateIds = [first, second]
            });
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)removed.Items[0].Outcome);
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)removed.Items[1].Outcome);
            Assert.Null((await GetStoredAsync(first)).ShortlistedAt);
            Assert.Null((await GetStoredAsync(second)).ShortlistedAt);
            Assert.Equal(CrmProspectingCandidateStatus.PendingReview, (await GetStoredAsync(first)).Status);

            var removeReplay = await _service.UnshortlistCandidatesAsync(new CrmProspectingCandidateBulkDto
            {
                CandidateIds = [first]
            });
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)removeReplay.Items[0].Outcome);
            Assert.Null((await GetStoredAsync(first)).ShortlistedAt);
        }
    }

    [Fact]
    public async Task BulkOperations_RejectMalformedIdLists_AndDeduplicateRepeatedIds()
    {
        using (_tenant.Change(TenantId))
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _service.BulkReviewCandidatesAsync(
                new CrmProspectingCandidateBulkReviewDto { CandidateIds = [] }));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.BulkReviewCandidatesAsync(
                new CrmProspectingCandidateBulkReviewDto { CandidateIds = [0] }));
            var tooMany = Enumerable.Range(1, 201).Select(value => (long)value).ToArray();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.BulkConvertCandidatesAsync(
                new CrmProspectingCandidateBulkDto { CandidateIds = tooMany }));

            // A repeated id collapses to one item instead of a self-colliding second attempt.
            var runId = await SeedRunAsync();
            var candidate = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "d1");
            var deduped = await _service.BulkReviewCandidatesAsync(new CrmProspectingCandidateBulkReviewDto
            {
                CandidateIds = [candidate, candidate],
                Approve = false
            });
            Assert.Single(deduped.Items);
            Assert.Equal(CrmProspectingBulkOutcome.Success, (CrmProspectingBulkOutcome)deduped.Items[0].Outcome);
            Assert.Equal(CrmProspectingCandidateStatus.Rejected, (await GetStoredAsync(candidate)).Status);
        }
    }

    [Fact]
    public async Task BulkOperations_PropagateCancellationInsteadOfReportingFailedItems()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync();
            var candidate = await SeedCandidateAsync(runId, CrmProspectingCandidateStatus.PendingReview, "z1");
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _service.BulkReviewCandidatesAsync(
                    new CrmProspectingCandidateBulkReviewDto { CandidateIds = [candidate], Approve = true },
                    cancellation.Token));

            // The cancelled request committed nothing and produced no fake outcome row.
            Assert.Equal(CrmProspectingCandidateStatus.PendingReview, (await GetStoredAsync(candidate)).Status);
        }
    }

    private async Task<long> SeedRunAsync()
    {
        using (_tenant.Change(TenantId))
        {
            var campaign = await _campaigns.AddAsync(new SysCrmProspectingCampaign
            {
                Name = $"campaign-{Guid.NewGuid():N}",
                OwnerUserId = OwnerUserId
            });
            var run = await _runs.AddAsync(new SysCrmProspectingRun
            {
                CampaignId = campaign.BasicId,
                StrategyId = 201,
                StrategyVersion = 1,
                RequestKey = Guid.NewGuid().ToString("N"),
                PayloadHash = new string('A', 64),
                SnapshotSearchObjective = "Bulk operations integration test",
                SnapshotProviderPlanJson = "[\"fake\"]",
                Status = CrmProspectingRunStatus.Succeeded
            });
            return run.BasicId;
        }
    }

    private async Task<long> SeedCandidateAsync(
        long runId,
        CrmProspectingCandidateStatus status,
        string externalId,
        long tenantId = TenantId)
    {
        using (_tenant.Change(tenantId))
        {
            var added = await _candidates.AddAsync(new SysCrmProspectingCandidate
            {
                RunId = runId,
                ProviderCode = "provider-a",
                ExternalId = externalId,
                CompanyName = externalId,
                Status = status
            });
            return added.BasicId;
        }
    }

    private async Task<long> SeedLeadAsync()
    {
        using (_tenant.Change(TenantId))
        {
            var added = await _leads.AddAsync(new SysCrmLead
            {
                CompanyName = $"seeded-lead-{Guid.NewGuid():N}",
                Status = CrmLeadStatus.New
            });
            return added.BasicId;
        }
    }

    private async Task SetConvertedLeadAsync(long candidateId, long leadId)
    {
        using (_tenant.Change(TenantId))
        {
            await _db.Updateable<SysCrmProspectingCandidate>()
                .SetColumns(candidate => candidate.ConvertedLeadId == leadId)
                .Where(candidate => candidate.BasicId == candidateId)
                .ExecuteCommandAsync();
        }
    }

    private async Task<SysCrmProspectingCandidate> GetStoredAsync(long candidateId)
        => await _db.Queryable<SysCrmProspectingCandidate>()
            .ClearFilter()
            .FirstAsync(candidate => candidate.BasicId == candidateId);

    private async Task<int> CountLeadsAsync()
        => await _db.Queryable<SysCrmLead>().ClearFilter().CountAsync();

    public void Dispose()
    {
        _tenantScope.Dispose();
        AsyncLocalCurrentTenantAccessor.Instance.Current = null;
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static long ScopeId() => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;
}
