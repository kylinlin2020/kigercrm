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
/// Exercises the real CodeFirst evidence identity index against SQLite.
/// PostgreSQL migration, B-tree limits and transaction behavior remain separate validation gates.
/// </summary>
public sealed class CrmProspectingEvidenceIdentitySqliteIntegrationTests : IDisposable
{
    private const long TenantId = 8831;
    private readonly string _connectionString = $"Data Source=kiger-crm-evidence-identity-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();
    private readonly CrmProspectingEvidenceRepository _repository;

    public CrmProspectingEvidenceIdentitySqliteIntegrationTests()
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

        _db.CodeFirst.InitTables(typeof(SysCrmProspectingEvidence));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmProspectingEvidenceRepository(resolver.Object);
    }

    [Fact]
    public async Task EvidenceIdentity_ShouldRejectExactDuplicatesAndRemainTenantScoped()
    {
        using (_tenant.Change(TenantId))
        {
            await _repository.AddAsync(CreateEvidence("A"));
            await Assert.ThrowsAsync<SqlSugarException>(
                () => _repository.AddAsync(CreateEvidence("A")));

            // A distinct canonical source URL fingerprint is a distinct evidence identity.
            var distinctSource = await _repository.AddAsync(CreateEvidence("B"));
            Assert.Equal(new string('B', 64), distinctSource.SourceUrlHash);

            // The identity tuple includes RawPayloadHash and FieldName, not just the source URL.
            var changedPayload = await _repository.AddAsync(
                CreateEvidence("A", rawPayloadHash: new string('C', 64)));
            Assert.Equal(new string('C', 64), changedPayload.RawPayloadHash);

            var differentField = await _repository.AddAsync(
                CreateEvidence("A", fieldName: "company_website"));
            Assert.Equal("company_website", differentField.FieldName);

            var differentProviderRecord = await _repository.AddAsync(
                CreateEvidence("A", providerRecordId: "registry-record-2"));
            Assert.Equal("registry-record-2", differentProviderRecord.ProviderRecordId);
        }

        using (_tenant.Change(TenantId + 1))
        {
            var otherTenant = await _repository.AddAsync(CreateEvidence("A"));
            Assert.Equal(TenantId + 1, otherTenant.TenantId);
        }
    }

    [Fact]
    public async Task EvidenceUniqueCollision_ShouldRequireTransactionRollbackBeforeReconciliation()
    {
        using (_tenant.Change(TenantId))
        {
            _db.Ado.BeginTran();
            try
            {
                await _repository.AddAsync(CreateEvidence("D", candidateId: 99102));
                await Assert.ThrowsAsync<SqlSugarException>(
                    () => _repository.AddAsync(CreateEvidence("D", candidateId: 99102)));
            }
            finally
            {
                // Do not query or attempt another insert in the failed transaction.
                _db.Ado.RollbackTran();
            }

            var persisted = await _repository.GetByCandidateAsync(99102);
            Assert.Empty(persisted);
        }
    }

    private static SysCrmProspectingEvidence CreateEvidence(
        string sourceFingerprint,
        string rawPayloadHash = "",
        string fieldName = "company_name",
        string providerRecordId = "",
        long candidateId = 99101)
        => new()
        {
            CandidateId = candidateId,
            ProviderCode = "official-registry",
            ProviderRecordId = providerRecordId,
            FieldName = fieldName,
            SourceUrl = $"https://registry.example/company/{sourceFingerprint}",
            SourceUrlHash = new string(sourceFingerprint[0], 64),
            RawPayloadHash = rawPayloadHash,
            EvidenceType = "registry",
            VerificationStatus = "unverified",
            RetrievedAtUtc = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero)
        };

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
