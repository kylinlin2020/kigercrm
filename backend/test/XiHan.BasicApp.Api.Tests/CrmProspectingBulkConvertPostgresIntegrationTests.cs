// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Uow;
using XiHan.Framework.Uow.Options;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// PostgreSQL-gated two-session bulk conversion race: both sessions call
/// <c>BulkConvertCandidatesAsync</c> on the same Approved candidate at once. The conditional
/// claim must yield exactly one Lead, the candidate must settle on Converted (never stuck in
/// Converting), and every reported outcome must agree with that single persisted Lead.
/// The unit-of-work manager is a no-op mock here: the race itself lives in the conditional
/// claim, which executes per statement on two independent connections.
/// </summary>
public sealed class CrmProspectingBulkConvertPostgresIntegrationTests : IDisposable
{
    private const long TenantId = 9931;
    private const long OwnerUserId = 77;
    private readonly string _externalId = $"bulk-race-{Guid.NewGuid():N}";

    private readonly ProbeCurrentTenant _tenant1 = new();
    private readonly ProbeCurrentTenant _tenant2 = new();
    private readonly SqlSugarScope _db1;
    private readonly SqlSugarScope _db2;

    public static bool PostgresConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING"));

    public CrmProspectingBulkConvertPostgresIntegrationTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("KIGER_TEST_POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("PostgreSQL integration tests require KIGER_TEST_POSTGRES_CONNECTION_STRING.");

        _db1 = CreateClient(connectionString, _tenant1);
        _db2 = CreateClient(connectionString, _tenant2);

        _db1.CodeFirst.InitTables(
            typeof(SysCrmProspectingCampaign),
            typeof(SysCrmProspectingRun),
            typeof(SysCrmProspectingCandidate),
            typeof(SysCrmLead));
    }

    [Fact(SkipUnless = nameof(PostgresConfigured), Skip = "Requires KIGER_TEST_POSTGRES_CONNECTION_STRING.")]
    public async Task BulkConvert_TwoSessionsRaceOnOneApprovedCandidate_ExactlyOneLeadAndNoStuckState()
    {
        var (candidateId, expectedSourceReference) = await SeedApprovedCandidateAsync();
        var service1 = CreateService(_db1);
        var service2 = CreateService(_db2);
        var input = new CrmProspectingCandidateBulkDto { CandidateIds = [candidateId] };

        var (first, second) = await RaceAsync(
            () => WithTenant(_tenant1, () => service1.BulkConvertCandidatesAsync(input)),
            () => WithTenant(_tenant2, () => service2.BulkConvertCandidatesAsync(input)));

        var firstItem = Assert.Single(first.Items);
        var secondItem = Assert.Single(second.Items);

        // Exactly one Lead exists for this candidate's provider identity.
        var leads = await _db1.Queryable<SysCrmLead>()
            .ClearFilter()
            .Where(lead => lead.SourceReference == expectedSourceReference)
            .ToListAsync();
        var winnerLead = Assert.Single(leads);
        Assert.Equal(CrmLeadStatus.New, winnerLead.Status);

        // The candidate settled on Converted with the winner's Lead: no Converting limbo.
        var stored = await _db1.Queryable<SysCrmProspectingCandidate>()
            .ClearFilter()
            .FirstAsync(candidate => candidate.BasicId == candidateId);
        Assert.Equal(CrmProspectingCandidateStatus.Converted, stored.Status);
        Assert.Equal(winnerLead.BasicId, stored.ConvertedLeadId);

        // The winner always reports Success. Every Success or Skipped item carries the same
        // persisted Lead id; a losing item may honestly be Skipped (its pre-check ran after the
        // winner committed), Success (it observed the winner's Lead on reload) or Failed (it
        // observed the in-flight claim) — whichever it is, it must say why instead of inventing
        // a Lead of its own.
        var items = new[] { firstItem, secondItem };
        Assert.Contains(items, item => (CrmProspectingBulkOutcome)item.Outcome == CrmProspectingBulkOutcome.Success);
        foreach (var item in items)
        {
            var outcome = (CrmProspectingBulkOutcome)item.Outcome;
            if (outcome is CrmProspectingBulkOutcome.Success or CrmProspectingBulkOutcome.Skipped)
            {
                Assert.Equal(winnerLead.BasicId, item.LeadId);
            }
            else
            {
                Assert.Equal(CrmProspectingBulkOutcome.Failed, outcome);
                Assert.False(string.IsNullOrWhiteSpace(item.Reason), "A failed race item must carry its reason.");
            }
        }
    }

    private async Task<(long CandidateId, string SourceReference)> SeedApprovedCandidateAsync()
    {
        var resolver = CreateResolver(_db1);
        var campaigns = new CrmProspectingCampaignRepository(resolver.Object);
        var runs = new CrmProspectingRunRepository(resolver.Object);
        var candidates = new CrmProspectingCandidateRepository(resolver.Object);

        using (_tenant1.Change(TenantId))
        {
            var campaign = await campaigns.AddAsync(new SysCrmProspectingCampaign
            {
                Name = $"bulk-race-{Guid.NewGuid():N}",
                OwnerUserId = OwnerUserId
            });
            var run = await runs.AddAsync(new SysCrmProspectingRun
            {
                CampaignId = campaign.BasicId,
                StrategyId = 301,
                StrategyVersion = 1,
                RequestKey = Guid.NewGuid().ToString("N"),
                PayloadHash = new string('A', 64),
                SnapshotSearchObjective = "Bulk conversion race integration test",
                SnapshotProviderPlanJson = "[\"fake\"]",
                Status = CrmProspectingRunStatus.Succeeded
            });
            var candidate = await candidates.AddAsync(new SysCrmProspectingCandidate
            {
                RunId = run.BasicId,
                ProviderCode = "provider-a",
                ExternalId = _externalId,
                CompanyName = "race-candidate",
                Status = CrmProspectingCandidateStatus.Approved
            });
            return (candidate.BasicId, $"provider-a:{_externalId}");
        }
    }

    private static CrmProspectingAppService CreateService(SqlSugarScope client)
    {
        var resolver = CreateResolver(client);
        var campaigns = new CrmProspectingCampaignRepository(resolver.Object);
        var strategies = new CrmProspectingStrategyRepository(resolver.Object);
        var runs = new CrmProspectingRunRepository(resolver.Object);
        var candidates = new CrmProspectingCandidateRepository(resolver.Object);
        var evidence = new CrmProspectingEvidenceRepository(resolver.Object);
        var scores = new CrmProspectingScoreRepository(resolver.Object);
        var leads = new CrmLeadRepository(resolver.Object);
        var customers = new CrmCustomerRepository(resolver.Object);
        var contacts = new CrmContactRepository(resolver.Object);
        var extractionJobs = new CrmProspectingExtractionJobRepository(resolver.Object);

        var dataScope = new Mock<IUserDataScopeFilterService>();
        dataScope
            .Setup(value => value.ResolveAccessibleUsersAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(UserDataScopeFilter.Unlimited));

        // No-op field-level security: Moq returns completed tasks for unconfigured Task members.
        // Ensure*Async<TInput> is constrained to class/new(), so It.IsAnyType cannot be used
        // as its type argument (CS1955/CS0311).
        var fieldSecurity = new Mock<IFieldSecurityService>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(value => value.CompleteAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var unitOfWorkManager = new Mock<IUnitOfWorkManager>();
        unitOfWorkManager
            .Setup(value => value.Begin(It.IsAny<XiHanUnitOfWorkOptions>(), It.IsAny<bool>()))
            .Returns(unitOfWork.Object);

        return new CrmProspectingAppService(
            new CrmProspectingDomainService(campaigns, strategies, runs),
            campaigns,
            strategies,
            runs,
            candidates,
            evidence,
            scores,
            leads,
            new CrmLeadDomainService(leads, customers, contacts),
            fieldSecurity.Object,
            dataScope.Object,
            new Mock<IRedisDelayQueue<CrmProspectingRunMessage>>().Object,
            unitOfWorkManager.Object,
            extractionJobs,
            CrmProspectingProviderRegistry.Create([], []),
            new Mock<IConfiguration>().Object);
    }

    private static Mock<ISqlSugarClientResolver> CreateResolver(ISqlSugarClient client)
    {
        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(client);
        return resolver;
    }

    private static async Task<(T First, T Second)> RaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = Task.Run(async () => { await start.Task; return await first(); });
        var secondTask = Task.Run(async () => { await start.Task; return await second(); });
        start.SetResult();
        var results = await Task.WhenAll(firstTask, secondTask);
        return (results[0], results[1]);
    }

    private static async Task<T> WithTenant<T>(ProbeCurrentTenant tenant, Func<Task<T>> action)
    {
        using (tenant.Change(TenantId))
            return await action();
    }

    private static SqlSugarScope CreateClient(string connectionString, ProbeCurrentTenant tenant)
        => new(new ConnectionConfig
        {
            DbType = DbType.PostgreSQL,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        }, db =>
        {
            db.QueryFilter.AddTableFilter<ISoftDelete>(entity => !entity.IsDeleted);
            db.QueryFilter.AddTableFilter<IMultiTenantEntity>(entity => entity.TenantId == 0 || ScopeId(tenant) == entity.TenantId);
            db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(entity => ScopeId(tenant) == entity.TenantId);
            db.Aop.DataExecuting = (_, entityInfo) =>
            {
                if (entityInfo.OperationType == DataFilterType.InsertByObject)
                {
                    entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                    entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                }
            };
        });

    private static long ScopeId(ProbeCurrentTenant tenant) => tenant.Id ?? 0;

    public void Dispose()
    {
        _db1.Dispose();
        _db2.Dispose();
    }

    private sealed class ProbeCurrentTenant : ICurrentTenant
    {
        public bool IsAvailable => Id is > 0;
        public long? Id { get; private set; }
        public string? Name { get; private set; }

        public IDisposable Change(long? id, string? name = null)
        {
            var (previousId, previousName) = (Id, Name);
            (Id, Name) = (id, name);
            return new Restore(() => (Id, Name) = (previousId, previousName));
        }

        private sealed class Restore(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }
}
