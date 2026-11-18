// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Security.Users;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>M3: outreach is human-gated, suppression-aware and explainable in its effect review.</summary>
public sealed class CrmOutreachTests : IDisposable
{
    private const long TenantId = 9971;
    private const long OwnerUserId = 777;

    private readonly string _connectionString = $"Data Source=kiger-crm-outreach-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly Mock<IUserDataScopeFilterService> _dataScope = new();
    private readonly CrmOutreachAppService _service;
    private UserDataScopeFilter _scope = UserDataScopeFilter.Unlimited;

    public CrmOutreachTests()
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
                db.Aop.DataExecuting = (_, entityInfo) =>
                {
                    if (entityInfo.OperationType == DataFilterType.InsertByObject)
                    {
                        entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                        entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                    }
                };
            });
        _db.CodeFirst.InitTables(
            typeof(SysCrmProspectingCampaign), typeof(SysCrmProspectingRun), typeof(SysCrmProspectingCandidate),
            typeof(SysCrmOutreachSequence), typeof(SysCrmOutreachEnrollment), typeof(SysCrmOutreachTouchpoint));

        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        var sequences = new CrmOutreachSequenceRepository(_resolver.Object);
        var enrollments = new CrmOutreachEnrollmentRepository(_resolver.Object);
        var touchpoints = new CrmOutreachTouchpointRepository(_resolver.Object);
        var candidates = new CrmProspectingCandidateRepository(_resolver.Object);
        var campaigns = new CrmProspectingCampaignRepository(_resolver.Object);
        var runs = new CrmProspectingRunRepository(_resolver.Object);
        _dataScope
            .Setup(value => value.ResolveAccessibleUsersAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(_scope));
        var user = new Mock<ICurrentUser>();
        user.SetupGet(value => value.UserId).Returns(42);
        _service = new CrmOutreachAppService(
            sequences, enrollments, touchpoints, candidates, runs, campaigns, _dataScope.Object, user.Object);
    }

    [Fact]
    public void Policy_ShouldValidateStepsClassifyRepliesAndComputeNextAction()
    {
        var steps = CrmOutreachPolicy.ParseSteps(StepsJson());
        Assert.Equal(2, steps.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => CrmOutreachPolicy.ParseSteps("[]"));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrmOutreachPolicy.ParseSteps(
            """[{"channel":99,"delayDays":1,"template":"x"}]"""));

        Assert.Equal(CrmOutreachReplyClass.NotInterested, CrmOutreachPolicy.ClassifyReply("Please unsubscribe me"));
        Assert.Equal(CrmOutreachReplyClass.Interested, CrmOutreachPolicy.ClassifyReply("有兴趣，请发报价"));
        Assert.Equal(CrmOutreachReplyClass.OutOfOffice, CrmOutreachPolicy.ClassifyReply("I am out of office"));
        Assert.Equal(CrmOutreachReplyClass.Unknown, CrmOutreachPolicy.ClassifyReply("hello"));

        var enrollment = new SysCrmOutreachEnrollment { CurrentStep = 0, Status = CrmOutreachEnrollmentStatus.Active };
        var now = DateTimeOffset.UtcNow;
        var stop = CrmOutreachPolicy.NextAction(enrollment, steps, CrmOutreachReplyClass.NotInterested, now);
        Assert.Equal(CrmOutreachEnrollmentStatus.Suppressed, stop.Status);
        Assert.Equal("reply_not_interested", stop.SuppressionReason);
        var advance = CrmOutreachPolicy.NextAction(enrollment, steps, CrmOutreachReplyClass.Interested, now);
        Assert.Equal(1, advance.NextStep);
        Assert.Equal(now.AddDays(1), advance.NextActionAtUtc);
        var ooo = CrmOutreachPolicy.NextAction(enrollment, steps, CrmOutreachReplyClass.OutOfOffice, now);
        Assert.Equal(0, ooo.NextStep);
        Assert.Equal(now.AddDays(7), ooo.NextActionAtUtc);
        var done = CrmOutreachPolicy.NextAction(
            new SysCrmOutreachEnrollment { CurrentStep = 1 }, steps, CrmOutreachReplyClass.None, now);
        Assert.Equal(CrmOutreachEnrollmentStatus.Completed, done.Status);
    }

    [Fact]
    public async Task Outreach_ShouldRequireHumanApprovalBeforeAnySentState()
    {
        var candidateId = await SeedCandidateAsync();
        var sequence = await _service.CreateSequenceAsync(new CrmOutreachSequenceCreateDto
        {
            Name = "intro-sequence",
            Steps = ParseStepsDto()
        });
        var active = await _service.ActivateSequenceAsync(sequence.BasicId);
        Assert.Equal((int)CrmOutreachSequenceStatus.Active, active.Status);

        var enrollment = await _service.EnrollCandidateAsync(new CrmOutreachEnrollDto
        {
            CandidateId = candidateId,
            SequenceId = sequence.BasicId
        });
        var replay = await _service.EnrollCandidateAsync(new CrmOutreachEnrollDto
        {
            CandidateId = candidateId,
            SequenceId = sequence.BasicId
        });
        Assert.Equal(enrollment.BasicId, replay.BasicId);

        var planned = Assert.Single(await _service.GetTouchpointsAsync(enrollment.BasicId));
        Assert.Equal((int)CrmOutreachTouchpointState.Planned, planned.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RecordTouchpointAsync(
            new CrmOutreachRecordDto { TouchpointId = planned.BasicId, State = (int)CrmOutreachTouchpointState.Sent }));

        var approved = await _service.ApproveTouchpointAsync(planned.BasicId);
        Assert.Equal((int)CrmOutreachTouchpointState.Approved, approved.State);
        Assert.Equal(42, approved.ApprovedBy);
        var sent = await _service.RecordTouchpointAsync(new CrmOutreachRecordDto
        {
            TouchpointId = planned.BasicId,
            State = (int)CrmOutreachTouchpointState.Sent
        });
        Assert.Equal((int)CrmOutreachTouchpointState.Sent, sent.State);

        var nextPlanned = Assert.Single((await _service.GetTouchpointsAsync(enrollment.BasicId))
            .Where(item => item.State == (int)CrmOutreachTouchpointState.Planned));
        Assert.Equal(1, nextPlanned.Step);
        var review = await _service.GetReviewAsync(sequence.BasicId);
        Assert.Equal(1, review.Enrollments);
        Assert.Equal(1, review.Sent);
    }

    [Fact]
    public async Task Outreach_ShouldSuppressOnOptOutAndRejectIneligibleCandidate()
    {
        var candidateId = await SeedCandidateAsync();
        var sequence = await _service.CreateSequenceAsync(new CrmOutreachSequenceCreateDto
        {
            Name = "optout-sequence",
            Steps = ParseStepsDto()
        });
        await _service.ActivateSequenceAsync(sequence.BasicId);
        var enrollment = await _service.EnrollCandidateAsync(new CrmOutreachEnrollDto
        {
            CandidateId = candidateId,
            SequenceId = sequence.BasicId
        });
        var suppressed = await _service.SuppressEnrollmentAsync(enrollment.BasicId, "requested by candidate");
        Assert.Equal((int)CrmOutreachEnrollmentStatus.Suppressed, suppressed.Status);
        Assert.All(await _service.GetTouchpointsAsync(enrollment.BasicId),
            item => Assert.Equal((int)CrmOutreachTouchpointState.Suppressed, item.State));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.EnrollCandidateAsync(
            new CrmOutreachEnrollDto { CandidateId = candidateId, SequenceId = sequence.BasicId }));

        var pendingId = await SeedCandidateAsync(CrmProspectingCandidateStatus.PendingReview);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.EnrollCandidateAsync(
            new CrmOutreachEnrollDto { CandidateId = pendingId, SequenceId = sequence.BasicId }));
    }

    [Fact]
    public void OutreachContract_ShouldCarryPermissionsAndNeverExposeASendCommand()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "AppServices", "CrmOutreachAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.46", "5.9.46.sql");
        Assert.Contains("[DynamicApi(Group = \"BasicApp.Crm\"", app, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]\n    public async Task<CrmOutreachTouchpointDto> ApproveTouchpointAsync", app, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]\n    public async Task<CrmOutreachTouchpointDto> RecordTouchpointAsync", app, StringComparison.Ordinal);
        Assert.DoesNotContain("SendEmailAsync", app, StringComparison.Ordinal);
        Assert.DoesNotContain("SmtpClient", app, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_outreach_touchpoint", migration, StringComparison.Ordinal);
    }

    private static string StepsJson()
        => """[{"channel":0,"delayDays":0,"template":"intro"},{"channel":3,"delayDays":3,"template":"follow-up"}]""";

    private static IReadOnlyList<CrmOutreachStepDto> ParseStepsDto()
        => System.Text.Json.JsonSerializer.Deserialize<List<CrmOutreachStepDto>>(
            StepsJson(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

    private async Task<long> SeedCandidateAsync(CrmProspectingCandidateStatus status = CrmProspectingCandidateStatus.Approved)
    {
        var campaign = await new CrmProspectingCampaignRepository(_resolver.Object).AddAsync(new SysCrmProspectingCampaign
        {
            Name = "outreach-campaign",
            OwnerUserId = OwnerUserId,
            Status = CrmProspectingCampaignStatus.Active
        });
        var run = await new CrmProspectingRunRepository(_resolver.Object).AddAsync(new SysCrmProspectingRun
        {
            CampaignId = campaign.BasicId,
            StrategyId = 1,
            StrategyVersion = 1,
            RequestKey = $"outreach-run-{Guid.NewGuid():N}",
            PayloadHash = new string('A', 64),
            SnapshotSearchObjective = "outreach",
            SnapshotProviderPlanJson = "[]"
        });
        var candidate = await new CrmProspectingCandidateRepository(_resolver.Object).AddAsync(new SysCrmProspectingCandidate
        {
            RunId = run.BasicId,
            ProviderCode = "provider-a",
            ExternalId = "ext-1",
            CompanyName = "ACME GmbH",
            BusinessEmail = "sales@acme.example.com",
            Status = status
        });
        return candidate.BasicId;
    }

    public void Dispose()
    {
        _tenantScope.Dispose();
        AsyncLocalCurrentTenantAccessor.Instance.Current = null;
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static long ScopeId() => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
