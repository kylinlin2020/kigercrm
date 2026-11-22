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
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Security.Users;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Customer pool: claim/release/assign/recover with exclusive updates and claim limits.</summary>
public sealed class CrmCustomerPoolTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=kiger-pool-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly CrmCustomerPoolAppService _service;
    private readonly CrmCustomerPoolRepository _entries;
    private readonly CrmCustomerRepository _customers;

    public CrmCustomerPoolTests()
    {
        _tenantScope = _tenant.Change(41);
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
        _db.CodeFirst.InitTables(typeof(SysCrmCustomerPoolEntry), typeof(SysCrmCustomer));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        var user = new Mock<ICurrentUser>();
        user.SetupGet(value => value.UserId).Returns(1001);
        _entries = new CrmCustomerPoolRepository(_resolver.Object);
        _customers = new CrmCustomerRepository(_resolver.Object);
        _service = new CrmCustomerPoolAppService(_entries, _customers, user.Object);
    }

    [Fact]
    public void Policy_ShouldGuardClaimReleaseAndIdle()
    {
        var now = DateTimeOffset.UtcNow;
        var available = new SysCrmCustomerPoolEntry { Status = CrmCustomerPoolStatus.Available, ClaimCount = 0 };
        Assert.True(CrmCustomerPoolPolicy.CanClaim(available, now).Allowed);
        var exhaustedAvailable = new SysCrmCustomerPoolEntry
        {
            Status = CrmCustomerPoolStatus.Available,
            ClaimCount = CrmCustomerPoolPolicy.MaximumClaims
        };
        Assert.False(CrmCustomerPoolPolicy.CanClaim(exhaustedAvailable, now).Allowed);

        var claimed = new SysCrmCustomerPoolEntry
        {
            Status = CrmCustomerPoolStatus.Claimed,
            ClaimedUserId = 1,
            ClaimedAtUtc = now
        };
        Assert.False(CrmCustomerPoolPolicy.CanClaim(claimed, now).Allowed);
        Assert.True(CrmCustomerPoolPolicy.CanRelease(claimed, 1).Allowed);
        Assert.False(CrmCustomerPoolPolicy.CanRelease(claimed, 2).Allowed);
        Assert.False(CrmCustomerPoolPolicy.IsIdle(claimed, now));
        Assert.True(CrmCustomerPoolPolicy.IsIdle(claimed, now.AddDays(8)));
        Assert.True(CrmCustomerPoolPolicy.CanClaim(claimed, now.AddDays(8)).Allowed);
        var exhausted = new SysCrmCustomerPoolEntry
        {
            Status = CrmCustomerPoolStatus.Available,
            ClaimCount = CrmCustomerPoolPolicy.MaximumClaims
        };
        Assert.False(CrmCustomerPoolPolicy.CanClaim(exhausted, now).Allowed);
    }

    [Fact]
    public async Task Flow_ShouldAddClaimReleaseAssignAndRecover()
    {
        var customer = await _customers.AddAsync(new SysCrmCustomer { Name = "pool-customer" });
        var added = await _service.AddAsync(new CrmCustomerPoolAddDto { CustomerId = customer.BasicId });
        Assert.Equal((int)CrmCustomerPoolStatus.Available, added.Status);
        Assert.Equal("pool-customer", added.CustomerName);

        var again = await _service.AddAsync(new CrmCustomerPoolAddDto { CustomerId = customer.BasicId });
        Assert.Equal(added.BasicId, again.BasicId);
        Assert.Single(await _service.GetAvailableAsync());

        var claimed = await _service.ClaimAsync(new CrmCustomerPoolActionDto { EntryId = added.BasicId });
        Assert.Equal((int)CrmCustomerPoolStatus.Claimed, claimed.Status);
        Assert.Equal(1001, claimed.ClaimedUserId);
        Assert.Equal(1, claimed.ClaimCount);
        Assert.Empty(await _service.GetAvailableAsync());
        Assert.Single(await _service.GetMineAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ClaimAsync(new CrmCustomerPoolActionDto { EntryId = added.BasicId }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AssignAsync(added.BasicId, 2002));

        var released = await _service.ReleaseAsync(new CrmCustomerPoolActionDto { EntryId = added.BasicId });
        Assert.Equal((int)CrmCustomerPoolStatus.Available, released.Status);
        Assert.NotNull(released.LastReleasedAtUtc);

        var assigned = await _service.AssignAsync(added.BasicId, 2002);
        Assert.Equal((int)CrmCustomerPoolStatus.Claimed, assigned.Status);
        Assert.Equal(2002, assigned.ClaimedUserId);

        // Make the claim idle and recover it.
        await _db.Updateable<SysCrmCustomerPoolEntry>()
            .SetColumns(item => item.ClaimedAtUtc == DateTimeOffset.UtcNow.AddDays(-8))
            .Where(item => item.BasicId == added.BasicId)
            .ExecuteCommandAsync();
        var recovered = await _service.RecoverIdleAsync();
        Assert.Equal(1, recovered);
        var final = await _entries.GetByIdAsync(added.BasicId);
        Assert.Equal(CrmCustomerPoolStatus.Recovered, final!.Status);
    }

    [Fact]
    public void Contract_ShouldGuardPermissionsAndMigration()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "AppServices", "CrmCustomerPoolAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.61", "5.9.61.sql");
        Assert.Contains("Tag = \"CustomerPool\"", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", service, StringComparison.Ordinal);
        Assert.Contains("TryClaimAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", service, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_customer_pool_entry", migration, StringComparison.Ordinal);
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
