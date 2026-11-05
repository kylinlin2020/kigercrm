using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Exercises provider request attempt persistence, terminal transitions and tenant isolation using real SqlSugar CodeFirst.
/// PostgreSQL concurrent insert races and migration execution remain separate acceptance gates.
/// </summary>
public sealed class CrmProspectingProviderRequestLedgerSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 76123;
    private readonly string _connectionString = $"Data Source=kiger-crm-provider-ledger-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();
    private readonly CrmProspectingProviderRequestRepository _repository;

    public CrmProspectingProviderRequestLedgerSqliteIntegrationTests()
    {
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

        _db.CodeFirst.InitTables(typeof(SysCrmProspectingProviderRequest));
        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmProspectingProviderRequestRepository(resolver.Object);
    }

    [Fact]
    public async Task CreateStarted_ShouldAllocateSequentialAttemptsAndIsolateTenants()
    {
        using (_tenant.Change(TenantId))
        {
            var first = await _repository.CreateStartedAsync(501, "fake", new string('A', 64), DateTimeOffset.UtcNow);
            var second = await _repository.CreateStartedAsync(501, "fake", new string('B', 64), DateTimeOffset.UtcNow);
            Assert.Equal(1, first.AttemptNo);
            Assert.Equal(2, second.AttemptNo);
            Assert.Equal("Started", first.State);
            Assert.Equal(new string('A', 64), first.RequestKey);
            Assert.Equal(2, (await _repository.GetListAsync(row => row.RunId == 501)).Count);
        }

        using (_tenant.Change(TenantId + 1))
        {
            var otherTenant = await _repository.CreateStartedAsync(501, "fake", new string('C', 64), DateTimeOffset.UtcNow);
            Assert.Equal(1, otherTenant.AttemptNo);
            Assert.Equal(TenantId + 1, otherTenant.TenantId);
            Assert.Equal(1, (await _repository.GetListAsync(row => row.RunId == 501)).Count);
        }
    }

    [Fact]
    public async Task Finalize_ShouldTransitionStartedExactlyOnceAndKeepBoundedDiagnostics()
    {
        using (_tenant.Change(TenantId))
        {
            var attempt = await _repository.CreateStartedAsync(502, "official-source", new string('D', 64), DateTimeOffset.UtcNow);
            var completedAt = DateTimeOffset.UtcNow;

            Assert.True(await _repository.TryFinalizeAsync(
                attempt.BasicId,
                "Failed",
                completedAt,
                durationMs: 123,
                resultCount: 4,
                failureCode: "provider_exception",
                failureSummary: "Authorization: Bearer bearer-secret api_key=key-secret Request failed: https://api.example.test/v1/search?token=url-secret"));

            Assert.False(await _repository.TryFinalizeAsync(
                attempt.BasicId,
                "Succeeded",
                completedAt.AddSeconds(1),
                durationMs: 456,
                resultCount: 99));

            var persisted = await _repository.GetByIdAsync(attempt.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal("Failed", persisted.State);
            Assert.Equal(123, persisted.DurationMs);
            Assert.Equal(4, persisted.ResultCount);
            Assert.Equal("provider_exception", persisted.FailureCode);
            Assert.DoesNotContain("bearer-secret", persisted.FailureSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("key-secret", persisted.FailureSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("url-secret", persisted.FailureSummary, StringComparison.Ordinal);
            Assert.Equal(completedAt, persisted.CompletedAt);
        }
    }

    [Fact]
    public async Task GetAttempt_ShouldRequireExactTenantRunProviderAndAttemptIdentity()
    {
        long attemptId;
        using (_tenant.Change(TenantId))
        {
            var attempt = await _repository.CreateStartedAsync(503, "source-a", new string('E', 64), DateTimeOffset.UtcNow);
            attemptId = attempt.BasicId;
            Assert.Equal(attemptId, (await _repository.GetAttemptAsync(503, "source-a", 1))?.BasicId);
            Assert.Null(await _repository.GetAttemptAsync(503, "source-b", 1));
        }

        using (_tenant.Change(TenantId + 1))
            Assert.Null(await _repository.GetAttemptAsync(503, "source-a", 1));
    }

    [Fact]
    public async Task CreateStarted_ShouldRejectInvalidFingerprintAndIdentity()
    {
        using (_tenant.Change(TenantId))
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _repository.CreateStartedAsync(504, "fake", "not-a-hash", DateTimeOffset.UtcNow));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _repository.CreateStartedAsync(0, "fake", new string('F', 64), DateTimeOffset.UtcNow));
        }
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
