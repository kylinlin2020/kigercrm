using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Exercises append-only request ledger lifecycle and tenant filtering with real SqlSugar/SQLite storage.</summary>
public sealed class CrmProspectingProviderRequestRepositorySqliteTests : IDisposable
{
    private const long TenantId = 9843;
    private readonly string _connectionString = $"Data Source=kiger-crm-provider-ledger-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();
    private readonly CrmProspectingProviderRequestRepository _repository;

    public CrmProspectingProviderRequestRepositorySqliteTests()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        var tenant = _tenant;
        _db = new SqlSugarScope(new ConnectionConfig
        {
            DbType = DbType.Sqlite,
            ConnectionString = _connectionString,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        }, db =>
        {
            db.QueryFilter.AddTableFilter<XiHan.Framework.MultiTenancy.IStrictMultiTenantEntity>(entity => ScopeId(tenant) == entity.TenantId);
            db.Aop.DataExecuting = (_, entityInfo) =>
            {
                if (entityInfo.OperationType == DataFilterType.InsertByObject)
                {
                    entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                    entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                }
            };
        });
        _db.CodeFirst.InitTables(typeof(SysCrmProspectingProviderRequest));
        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmProspectingProviderRequestRepository(resolver.Object);
    }

    [Fact]
    public async Task StartedAttempt_ShouldPersistBeforeItCanBeFinalized()
    {
        using (_tenant.Change(TenantId))
        {
            var startedAt = DateTimeOffset.UtcNow;
            var created = await _repository.CreateStartedAsync(1001, "fake", new string('A', 64), startedAt);
            Assert.True(created.BasicId > 0);
            Assert.Equal(1, created.AttemptNo);
            Assert.Equal("Started", created.State);
            Assert.Equal(startedAt, (await _repository.GetAttemptAsync(1001, "fake", 1))?.StartedAt);
        }
    }

    [Fact]
    public async Task AttemptNumbers_ShouldIncreaseForEachProviderWithinRun()
    {
        using (_tenant.Change(TenantId))
        {
            var first = await _repository.CreateStartedAsync(1002, "fake", new string('A', 64), DateTimeOffset.UtcNow);
            var second = await _repository.CreateStartedAsync(1002, "fake", new string('B', 64), DateTimeOffset.UtcNow);
            var otherProvider = await _repository.CreateStartedAsync(1002, "ted", new string('C', 64), DateTimeOffset.UtcNow);
            Assert.Equal(1, first.AttemptNo);
            Assert.Equal(2, second.AttemptNo);
            Assert.Equal(1, otherProvider.AttemptNo);
        }
    }

    [Fact]
    public async Task Finalization_ShouldBeExactlyOnceAndBoundDiagnostics()
    {
        using (_tenant.Change(TenantId))
        {
            var row = await _repository.CreateStartedAsync(1003, "fake", new string('D', 64), DateTimeOffset.UtcNow);
            var completedAt = DateTimeOffset.UtcNow;
            Assert.True(await _repository.TryFinalizeAsync(row.BasicId, "Failed", completedAt, 123, httpStatus: 503,
                failureCode: new string('E', 100), failureSummary: new string('x', 1200)));
            Assert.False(await _repository.TryFinalizeAsync(row.BasicId, "Succeeded", completedAt, 1, resultCount: 4));
            var persisted = await _repository.GetAttemptAsync(1003, "fake", 1);
            Assert.NotNull(persisted);
            Assert.Equal("Failed", persisted.State);
            Assert.Equal(80, persisted.FailureCode?.Length);
            Assert.Equal(1000, persisted.FailureSummary?.Length);
            Assert.Equal(503, persisted.HttpStatus);
            Assert.Equal(123, persisted.DurationMs);
        }
    }

    [Fact]
    public async Task Finalization_ShouldRejectNonTerminalStatesAndInvalidHashes()
    {
        using (_tenant.Change(TenantId))
        {
            var row = await _repository.CreateStartedAsync(1004, "fake", new string('F', 64), DateTimeOffset.UtcNow);
            await Assert.ThrowsAsync<ArgumentException>(() => _repository.TryFinalizeAsync(row.BasicId, "Started", DateTimeOffset.UtcNow, 1));
            await Assert.ThrowsAsync<ArgumentException>(() => _repository.TryFinalizeAsync(row.BasicId, "Succeeded", DateTimeOffset.UtcNow, 1, responseHash: "not-a-hash"));
        }
    }

    [Fact]
    public async Task ProviderCooldown_ShouldPersistAcrossRunsAndRemainTenantScoped()
    {
        var now = DateTimeOffset.UtcNow;
        var retryAt = now.AddMinutes(4);
        using (_tenant.Change(TenantId))
        {
            var row = await _repository.CreateStartedAsync(2001, "ted", new string('3', 64), now);
            Assert.True(await _repository.TryFinalizeAsync(
                row.BasicId, "RateLimited", now.AddSeconds(1), 100,
                httpStatus: 429, failureCode: "provider_rate_limited", retryAfterAt: retryAt));

            Assert.Equal(retryAt, await _repository.GetActiveCooldownUntilAsync("ted", now.AddMinutes(1)));
            Assert.Null(await _repository.GetActiveCooldownUntilAsync("ted", retryAt.AddSeconds(1)));
        }

        using (_tenant.Change(TenantId + 1))
            Assert.Null(await _repository.GetActiveCooldownUntilAsync("ted", now.AddMinutes(1)));

        using (_tenant.Change(TenantId))
            Assert.Equal(retryAt, await _repository.GetActiveCooldownUntilAsync("ted", now.AddMinutes(2)));
    }

    [Fact]
    public async Task FailureDiagnostics_ShouldRedactUrlQueryAndFragmentSecrets()
    {
        using (_tenant.Change(TenantId))
        {
            var row = await _repository.CreateStartedAsync(1006, "fake", new string('2', 64), DateTimeOffset.UtcNow);
            Assert.True(await _repository.TryFinalizeAsync(
                row.BasicId, "Failed", DateTimeOffset.UtcNow, 3,
                failureSummary: "Provider error at https://example.test/path?token=secret#private Authorization: Bearer supersecret api_key=abcdef"));
            var persisted = await _repository.GetAttemptAsync(1006, "fake", 1);
            Assert.NotNull(persisted);
            Assert.Contains("https://example.test/path", persisted.FailureSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", persisted.FailureSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("private", persisted.FailureSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("supersecret", persisted.FailureSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("abcdef", persisted.FailureSummary, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AttemptReads_ShouldNotCrossTenantBoundary()
    {
        long requestId;
        using (_tenant.Change(TenantId))
            requestId = (await _repository.CreateStartedAsync(1005, "fake", new string('1', 64), DateTimeOffset.UtcNow)).BasicId;

        using (_tenant.Change(TenantId + 1))
        {
            Assert.Null(await _repository.GetAttemptAsync(1005, "fake", 1));
            Assert.False(await _repository.TryFinalizeAsync(requestId, "Succeeded", DateTimeOffset.UtcNow, 1));
        }
        using (_tenant.Change(TenantId))
            Assert.Equal("Started", (await _repository.GetAttemptAsync(1005, "fake", 1))?.State);
    }

    public void Dispose()
    {
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static long ScopeId(ProbeCurrentTenant tenant) => tenant.Id ?? 0;

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
