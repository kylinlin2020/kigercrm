using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Exercises the actual candidate repository conditional UPDATE against SQLite.
/// This verifies the sequential claim boundary, not PostgreSQL two-connection race semantics.
/// </summary>
public sealed class CrmProspectingCandidateClaimSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 9842;
    private readonly string _connectionString = $"Data Source=kiger-crm-candidate-claim-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();
    private readonly CrmProspectingCandidateRepository _repository;

    public CrmProspectingCandidateClaimSqliteIntegrationTests()
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

        _db.CodeFirst.InitTables(typeof(SysCrmProspectingCandidate));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmProspectingCandidateRepository(resolver.Object);
    }

    [Fact]
    public async Task ApprovedCandidate_ShouldOnlyBeClaimedOnce()
    {
        using (_tenant.Change(TenantId))
        {
            var candidate = await _repository.AddAsync(new SysCrmProspectingCandidate
            {
                RunId = 7001,
                ProviderCode = "fake",
                ExternalId = "candidate-claim-001",
                CompanyName = "Example Industrial Systems",
                Status = CrmProspectingCandidateStatus.Approved
            });

            Assert.True(await _repository.TryClaimForConversionAsync(candidate.BasicId));

            var persisted = await _repository.GetByIdAsync(candidate.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingCandidateStatus.Converting, persisted.Status);

            // A duplicate delivery/request must lose because the predicate requires Approved.
            Assert.False(await _repository.TryClaimForConversionAsync(candidate.BasicId));

            persisted = await _repository.GetByIdAsync(candidate.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingCandidateStatus.Converting, persisted.Status);
        }
    }

    [Fact]
    public async Task CandidateClaim_ShouldRemainStrictlyTenantScoped()
    {
        long candidateId;
        using (_tenant.Change(TenantId))
        {
            var candidate = await _repository.AddAsync(new SysCrmProspectingCandidate
            {
                RunId = 7002,
                ProviderCode = "fake",
                ExternalId = "candidate-tenant-001",
                CompanyName = "Tenant Scoped Example",
                Status = CrmProspectingCandidateStatus.Approved
            });
            candidateId = candidate.BasicId;
        }

        using (_tenant.Change(TenantId + 1))
        {
            Assert.False(await _repository.TryClaimForConversionAsync(candidateId));
        }

        using (_tenant.Change(TenantId))
        {
            var persisted = await _repository.GetByIdAsync(candidateId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingCandidateStatus.Approved, persisted.Status);
        }
    }

    [Fact]
    public async Task CandidateReview_ShouldBeConditionalAndRequireDuplicateOverride()
    {
        using (_tenant.Change(TenantId))
        {
            var pending = await _repository.AddAsync(new SysCrmProspectingCandidate
            {
                RunId = 7003,
                ProviderCode = "official",
                ExternalId = "candidate-review-pending",
                CompanyName = "Pending Review Example",
                Status = CrmProspectingCandidateStatus.PendingReview
            });

            Assert.True(await _repository.TryReviewAsync(pending.BasicId, approve: true, overrideDuplicate: false));
            Assert.False(await _repository.TryReviewAsync(pending.BasicId, approve: false, overrideDuplicate: false));

            var reviewed = await _repository.GetByIdAsync(pending.BasicId);
            Assert.NotNull(reviewed);
            Assert.Equal(CrmProspectingCandidateStatus.Approved, reviewed.Status);

            var duplicate = await _repository.AddAsync(new SysCrmProspectingCandidate
            {
                RunId = 7003,
                ProviderCode = "official",
                ExternalId = "candidate-review-duplicate",
                CompanyName = "Duplicate Review Example",
                Status = CrmProspectingCandidateStatus.Duplicate
            });

            Assert.False(await _repository.TryReviewAsync(duplicate.BasicId, approve: true, overrideDuplicate: false));
            Assert.True(await _repository.TryReviewAsync(duplicate.BasicId, approve: true, overrideDuplicate: true));

            var overridden = await _repository.GetByIdAsync(duplicate.BasicId);
            Assert.NotNull(overridden);
            Assert.Equal(CrmProspectingCandidateStatus.Approved, overridden.Status);
        }
    }

    [Fact]
    public async Task CandidateReview_ShouldAllowRejectingDuplicateWithoutOverride()
    {
        using (_tenant.Change(TenantId))
        {
            var duplicate = await _repository.AddAsync(new SysCrmProspectingCandidate
            {
                RunId = 7004,
                ProviderCode = "official",
                ExternalId = "candidate-review-reject-duplicate",
                CompanyName = "Duplicate Rejection Example",
                Status = CrmProspectingCandidateStatus.Duplicate
            });

            Assert.True(await _repository.TryReviewAsync(duplicate.BasicId, approve: false, overrideDuplicate: false));

            var persisted = await _repository.GetByIdAsync(duplicate.BasicId);
            Assert.NotNull(persisted);
            Assert.Equal(CrmProspectingCandidateStatus.Rejected, persisted.Status);
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
