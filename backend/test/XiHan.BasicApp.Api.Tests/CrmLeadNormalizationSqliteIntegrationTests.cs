using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Exercises the real Lead domain write path and SqlSugar persistence against an in-memory SQLite database.
/// PostgreSQL migration execution remains a separate provider-specific validation gate.
/// </summary>
public sealed class CrmLeadNormalizationSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 73;

    private readonly string _connectionString = $"Data Source=kiger-crm-lead-normalization-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly ProbeCurrentTenant _tenant = new();
    private readonly CrmLeadDomainService _service;
    private readonly CrmLeadRepository _repository;

    public CrmLeadNormalizationSqliteIntegrationTests()
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
                db.QueryFilter.AddTableFilter<IMultiTenantEntity>(entity => entity.TenantId == 0 || entity.TenantId == ScopeId(tenant));
                db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(entity => entity.TenantId == ScopeId(tenant));
                db.Aop.DataExecuting = (_, entityInfo) =>
                {
                    if (entityInfo.OperationType == DataFilterType.InsertByObject)
                    {
                        entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                        entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                    }
                };
            });

        _db.CodeFirst.InitTables(typeof(SysCrmLead));

        var resolver = new Mock<ISqlSugarClientResolver>();
        resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _repository = new CrmLeadRepository(resolver.Object);
        _service = new CrmLeadDomainService(
            _repository,
            new Mock<ICrmCustomerRepository>().Object,
            new Mock<ICrmContactRepository>().Object);
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistNormalizedEmailPhoneAndWebsiteHost()
    {
        using (_tenant.Change(TenantId))
        {
            var created = await _service.CreateAsync(new CrmLeadCreateCommand(
                "Acme Industrial",
                "Jordan Smith",
                " Sales@Acme.COM ",
                "+1 (415) 555-0123",
                "https://www.acme.example/products/catalog",
                "US",
                "Website",
                null,
                null,
                null));

            var stored = await _db.Queryable<SysCrmLead>()
                .SingleAsync(lead => lead.BasicId == created.BasicId);

            Assert.Equal("sales@acme.com", stored.DuplicateEmailNormalized);
            Assert.Equal("14155550123", stored.PhoneDigits);
            Assert.Equal("acme.example", stored.WebsiteHost);

            Assert.Equal(1, await _db.Queryable<SysCrmLead>()
                .CountAsync(lead => lead.PhoneDigits == "14155550123"));
            Assert.Equal(1, await _db.Queryable<SysCrmLead>()
                .CountAsync(lead => lead.WebsiteHost == "acme.example"));
            Assert.Equal(CrmLeadStatus.New, stored.Status);
            Assert.Equal(TenantId, stored.TenantId);

            var updated = await _service.UpdateAsync(new CrmLeadUpdateCommand(
                created.BasicId,
                "Acme Industrial",
                "Jordan Smith",
                " SALES@ACME.EXAMPLE ",
                "+1-212-555-0199",
                "http://www.acme.example/about",
                "US",
                "Website",
                null,
                (int)CrmLeadStatus.Qualified,
                null,
                null));

            Assert.Equal("sales@acme.example", updated.DuplicateEmailNormalized);
            Assert.Equal("12125550199", updated.PhoneDigits);
            Assert.Equal("acme.example", updated.WebsiteHost);
        }
    }

    [Fact]
    public async Task GetDuplicateMatchesAsync_ShouldBatchMatchEmailAndWebsiteWithinTenant()
    {
        using (_tenant.Change(TenantId))
        {
            await _service.CreateAsync(new CrmLeadCreateCommand(
                "Email Match Ltd", "Alex", "sales@email-match.example", null, null,
                "US", "Website", null, null, null));
            await _service.CreateAsync(new CrmLeadCreateCommand(
                "Website Match Ltd", "Sam", null, null, "https://www.website-match.example",
                "US", "Website", null, null, null));
            await _service.CreateAsync(new CrmLeadCreateCommand(
                "Unrelated Ltd", "Taylor", "other@unrelated.example", null, "https://unrelated.example",
                "US", "Website", null, null, null));

            var matches = await _repository.GetDuplicateMatchesAsync(
                new[] { "sales@email-match.example" },
                new[] { "website-match.example" });

            Assert.Equal(2, matches.Count);
            Assert.Contains(matches, lead => lead.CompanyName == "Email Match Ltd");
            Assert.Contains(matches, lead => lead.CompanyName == "Website Match Ltd");
            Assert.DoesNotContain(matches, lead => lead.CompanyName == "Unrelated Ltd");
        }

        using (_tenant.Change(TenantId + 1))
        {
            var crossTenantMatches = await _repository.GetDuplicateMatchesAsync(
                new[] { "sales@email-match.example" },
                new[] { "website-match.example" });
            Assert.Empty(crossTenantMatches);
        }
    }

    [Fact]
    public async Task GetDuplicateMatchesAsync_ShouldProcessKeysAcrossMultipleBatches()
    {
        using (_tenant.Change(TenantId))
        {
            await _service.CreateAsync(new CrmLeadCreateCommand(
                "Last Batch Match Ltd", "Casey", "last-batch@batch-match.example", null, null,
                "US", "Website", null, null, null));

            // The repository chunks lookup keys at 300. Put the only matching key
            // in the second batch so the test exercises more than the first query.
            var emails = Enumerable.Range(0, 300)
                .Select(index => $"unmatched-{index}@batch.example")
                .Append("last-batch@batch-match.example")
                .ToArray();

            var matches = await _repository.GetDuplicateMatchesAsync(emails, Array.Empty<string>());

            var match = Assert.Single(matches);
            Assert.Equal("Last Batch Match Ltd", match.CompanyName);
        }
    }

    [Fact]
    public async Task GetDuplicateMatchesAsync_ShouldHonorCancellationBeforeQuerying()
    {
        using (_tenant.Change(TenantId))
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _repository.GetDuplicateMatchesAsync(
                    new[] { "cancel@example.test" },
                    Array.Empty<string>(),
                    cancellation.Token));
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
