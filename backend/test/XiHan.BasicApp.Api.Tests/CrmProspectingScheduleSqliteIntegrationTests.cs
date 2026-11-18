// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>M1.4: schedule approval lifecycle and worker-only dispatch reusing the approved StartRun pipeline.</summary>
public sealed class CrmProspectingScheduleSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 9951;
    private const long OtherTenantId = 9952;
    private const long OwnerUserId = 123;

    private readonly string _connectionString = $"Data Source=kiger-crm-schedule-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly Mock<IUserDataScopeFilterService> _dataScope = new();
    private readonly CrmProspectingCampaignRepository _campaigns;
    private readonly CrmProspectingScheduleRepository _schedules;
    private readonly CrmProspectingScheduleAppService _service;
    private UserDataScopeFilter _scope = UserDataScopeFilter.Unlimited;

    public CrmProspectingScheduleSqliteIntegrationTests()
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
        _db.CodeFirst.InitTables(typeof(SysCrmProspectingCampaign), typeof(SysCrmProspectingSchedule));

        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _campaigns = new CrmProspectingCampaignRepository(_resolver.Object);
        _schedules = new CrmProspectingScheduleRepository(_resolver.Object);
        _dataScope
            .Setup(value => value.ResolveAccessibleUsersAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(_scope));
        _service = new CrmProspectingScheduleAppService(
            _campaigns,
            _schedules,
            _dataScope.Object,
            new Mock<XiHan.Framework.Security.Users.ICurrentUser>().Object);
    }

    [Fact]
    public async Task Lifecycle_ShouldEnforceApprovalPauseResumeAndCancel()
    {
        var campaignId = await SeedCampaignAsync();
        var created = await _service.CreateScheduleAsync(ScheduleInput(campaignId));
        Assert.Equal(CrmProspectingScheduleStatus.Draft, (CrmProspectingScheduleStatus)created.Status);
        Assert.Null(created.NextRunAtUtc);

        var submitted = await _service.SubmitScheduleForApprovalAsync(created.BasicId);
        Assert.Equal(CrmProspectingScheduleStatus.PendingApproval, (CrmProspectingScheduleStatus)submitted.Status);

        var approved = await _service.DecideScheduleAsync(new CrmProspectingScheduleDecisionDto
        {
            ScheduleId = created.BasicId,
            Approve = true
        });
        Assert.Equal(CrmProspectingScheduleStatus.Approved, (CrmProspectingScheduleStatus)approved.Status);
        Assert.NotNull(approved.NextRunAtUtc);

        var paused = await _service.PauseScheduleAsync(created.BasicId);
        Assert.Equal(CrmProspectingScheduleStatus.Paused, (CrmProspectingScheduleStatus)paused.Status);
        Assert.Null(paused.NextRunAtUtc);
        Assert.Empty(await _schedules.GetDueAsync(DateTimeOffset.UtcNow, 10));

        var resumed = await _service.ResumeScheduleAsync(created.BasicId);
        Assert.Equal(CrmProspectingScheduleStatus.Approved, (CrmProspectingScheduleStatus)resumed.Status);
        Assert.NotNull(resumed.NextRunAtUtc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ResumeScheduleAsync(created.BasicId));

        var cancelled = await _service.CancelScheduleAsync(created.BasicId);
        Assert.Equal(CrmProspectingScheduleStatus.Cancelled, (CrmProspectingScheduleStatus)cancelled.Status);
        Assert.Null(cancelled.NextRunAtUtc);
    }

    [Fact]
    public async Task Create_ShouldBeIdempotentAndRejectForeignCampaignAndScope()
    {
        var campaignId = await SeedCampaignAsync();
        var foreignCampaign = await _campaigns.AddAsync(new SysCrmProspectingCampaign
        {
            Name = "foreign",
            OwnerUserId = OwnerUserId + 1,
            Status = CrmProspectingCampaignStatus.Active
        });
        var input = ScheduleInput(campaignId);
        var first = await _service.CreateScheduleAsync(input);
        var replay = await _service.CreateScheduleAsync(input);
        Assert.Equal(first.BasicId, replay.BasicId);
        Assert.Equal(1, await _db.Queryable<SysCrmProspectingSchedule>().ClearFilter().CountAsync());

        var changed = ScheduleInput(campaignId);
        changed.Name = "different name";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateScheduleAsync(changed));
        using (_tenant.Change(OtherTenantId))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.CreateScheduleAsync(ScheduleInput(foreignCampaign.BasicId)));
        }
        _scope = new UserDataScopeFilter(false, [OwnerUserId + 1]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateScheduleAsync(ScheduleInput(campaignId)));
    }

    [Fact]
    public async Task Dispatcher_ShouldCreateOneRunPerOccurrenceAndAdvanceViaCas()
    {
        var campaignId = await SeedCampaignAsync();
        var created = await _service.CreateScheduleAsync(ScheduleInput(campaignId));
        await _service.SubmitScheduleForApprovalAsync(created.BasicId);
        await _service.DecideScheduleAsync(new CrmProspectingScheduleDecisionDto { ScheduleId = created.BasicId, Approve = true });

        var now = DateTimeOffset.UtcNow;
        var stored = await _schedules.GetByIdAsync(created.BasicId);
        stored!.NextRunAtUtc = now.AddMinutes(-1);
        await _schedules.UpdateAsync(stored);

        var run = new CrmProspectingRunDto
        {
            BasicId = 777,
            CampaignId = campaignId,
            Status = (int)CrmProspectingRunStatus.Queued
        };
        var prospecting = new Mock<XiHan.BasicApp.Crm.Application.Contracts.ICrmProspectingAppService>();
        prospecting
            .Setup(value => value.StartRunAsync(It.IsAny<CrmProspectingRunStartDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);
        var dispatcher = new CrmProspectingScheduleDispatcher(
            _schedules, prospecting.Object, new Mock<ILogger<CrmProspectingScheduleDispatcher>>().Object);

        Assert.Equal(1, await dispatcher.DispatchDueAsync(now, 10));
        var advanced = await _schedules.GetByIdAsync(created.BasicId);
        Assert.NotNull(advanced!.NextRunAtUtc);
        // SQLite reattaches the local offset on DateTimeOffset round-trip; assert the configured
        // local wall clock and the compare-and-swap instead of comparing instants across the fixture.
        Assert.Equal(9, advanced.NextRunAtUtc.Value.Hour);
        Assert.Equal(0, advanced.NextRunAtUtc.Value.Minute);
        Assert.Equal(777, advanced.LastRunId);

        var observed = advanced.NextRunAtUtc.Value;
        Assert.True(await _schedules.TryClaimAsync(created.BasicId, observed, observed.AddDays(2), now, 888));
        Assert.False(await _schedules.TryClaimAsync(created.BasicId, observed, observed.AddDays(2), now, 889));

        prospecting.Verify(value => value.StartRunAsync(
            It.Is<CrmProspectingRunStartDto>(input =>
                input.CampaignId == campaignId && input.RequestKey.StartsWith("daily-", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ScheduleContract_ShouldAuthorizeCommandsAndKeepDispatchWorkerOnly()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "AppServices", "CrmProspectingScheduleAppService.cs");
        var worker = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Infrastructure", "Prospecting", "CrmProspectingScheduleHostedService.cs");
        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Extensions", "ServiceCollectionExtensions.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.44", "5.9.44.sql");

        Assert.Contains("[DynamicApi(Group = \"BasicApp.Crm\"", app, StringComparison.Ordinal);
        foreach (var method in new[]
                 {
                     "CreateScheduleAsync", "GetSchedulesAsync", "SubmitScheduleForApprovalAsync",
                     "DecideScheduleAsync", "PauseScheduleAsync", "ResumeScheduleAsync", "CancelScheduleAsync"
                 })
            Assert.Contains(method, app, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatchDueAsync", app, StringComparison.Ordinal);
        Assert.Contains("DispatchDueAsync", worker, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<CrmProspectingScheduleHostedService>()", registration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_prospecting_schedule", migration, StringComparison.Ordinal);
        Assert.Contains("ix_crm_prospecting_schedule_te_st_next", migration, StringComparison.Ordinal);
    }

    private async Task<long> SeedCampaignAsync()
    {
        var campaign = await _campaigns.AddAsync(new SysCrmProspectingCampaign
        {
            Name = $"schedule-campaign-{Guid.NewGuid():N}",
            OwnerUserId = OwnerUserId,
            Status = CrmProspectingCampaignStatus.Active
        });
        return campaign.BasicId;
    }

    private static CrmProspectingScheduleCreateDto ScheduleInput(long campaignId) => new()
    {
        CampaignId = campaignId,
        Name = "daily buyers scan",
        RequestKeyPrefix = "daily-scan",
        TimeZoneId = "UTC",
        Frequency = (int)CrmProspectingScheduleFrequency.Daily,
        Interval = 1,
        TimeOfDayMinutes = 9 * 60,
        StartDateUtc = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero)
    };

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
