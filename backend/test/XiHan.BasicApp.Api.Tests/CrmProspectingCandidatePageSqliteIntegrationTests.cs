// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Exporting;
using XiHan.BasicApp.Crm.Application.QueryServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.BasicApp.Saas.Application.Dtos;
using XiHan.BasicApp.Saas.Application.Exporting;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.BasicApp.Saas.Domain.Entities;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.Domain.Shared.Paging.Models;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Exercises the real <see cref="CrmProspectingCandidateQueryService"/> against SQLite with the
/// production repository stack: cross-page partitioning (no repeat / no loss) including the
/// soft-delete boundary, status filter combined with paging, stable tie-breaking on CreatedTime,
/// tenant isolation, the rejection paths, and export rows honoring the query snapshot filters.
/// </summary>
public sealed class CrmProspectingCandidatePageSqliteIntegrationTests : IDisposable
{
    private const long TenantId = 9842;
    private const long OtherTenantId = 9843;
    private const long OwnerUserId = 77;
    private const int PageSeedSize = 3;

    // Controlled CreatedTime groups: rows inside one group collide so the BasicId tie-break
    // is what keeps the ordering (and therefore the page boundary) deterministic.
    private static readonly DateTimeOffset TimeOldest = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TimeMiddle = TimeOldest.AddHours(1);
    private static readonly DateTimeOffset TimeNewest = TimeOldest.AddHours(2);

    private readonly string _connectionString = $"Data Source=kiger-crm-candidate-page-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    // Repository write guards read AsyncLocalCurrentTenantAccessor.Instance.Current;
    // use the framework CurrentTenant so filters, AOP auditing and the guard agree.
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly Mock<IUserDataScopeFilterService> _dataScope = new();
    private readonly CrmProspectingRunRepository _runs;
    private readonly CrmProspectingCampaignRepository _campaigns;
    private readonly CrmProspectingCandidateRepository _candidates;
    private readonly CrmProspectingCandidateQueryService _service;
    private UserDataScopeFilter _scope = UserDataScopeFilter.Unlimited;
    private int? _lastContextTotal;

    public CrmProspectingCandidatePageSqliteIntegrationTests()
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
                        // Row fixtures seed explicit audit values to control the paging order
                        // and the soft-delete boundary; only unseeded columns take the audit
                        // stamp (ToCreated would otherwise force IsDeleted back to false).
                        var seeded = entityInfo.PropertyName switch
                        {
                            nameof(SysCrmProspectingCandidate.CreatedTime)
                                => value is DateTimeOffset existing && existing != default,
                            nameof(SysCrmProspectingCandidate.IsDeleted)
                                => value is bool deleted && deleted,
                            _ => false
                        };
                        if (!seeded)
                        {
                            entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                        }
                    }
                };
            });

        _db.CodeFirst.InitTables(
            typeof(SysCrmProspectingCampaign),
            typeof(SysCrmProspectingRun),
            typeof(SysCrmProspectingCandidate));

        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _runs = new CrmProspectingRunRepository(_resolver.Object);
        _campaigns = new CrmProspectingCampaignRepository(_resolver.Object);
        _candidates = new CrmProspectingCandidateRepository(_resolver.Object);

        _dataScope
            .Setup(service => service.ResolveAccessibleUsersAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(_scope));
        _service = new CrmProspectingCandidateQueryService(_runs, _campaigns, _candidates, _dataScope.Object);
    }

    [Fact]
    public async Task CandidatePages_ShouldPartitionRowsWithoutRepeatOrLossAcrossTiesAndSoftDelete()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync(TenantId, OwnerUserId);
            var ids = new Dictionary<string, long>(StringComparer.Ordinal);

            // Insertion order fixes BasicId ascending; within a CreatedTime tie the stable
            // ordering must therefore yield reverse insertion order.
            ids["c1"] = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.PendingReview, "c1", TimeOldest);
            ids["c2"] = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.PendingReview, "c2", TimeOldest);
            ids["c3"] = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.Approved, "c3", TimeOldest);
            ids["c4"] = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.PendingReview, "c4", TimeMiddle);
            ids["c5"] = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.Duplicate, "c5", TimeMiddle);
            ids["c6"] = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.PendingReview, "c6", TimeNewest);
            ids["c7"] = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.Duplicate, "c7", TimeNewest);

            // Soft-deleted row sits inside the newest-but-one time group: if the IsDeleted
            // filter were missing it would surface in the middle of page 1/2.
            ids["deleted"] = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.PendingReview, "deleted", TimeMiddle, deleted: true);
            // Another tenant's row for the same RunId carries the newest time, so a broken
            // tenant filter would place it first in the very first page.
            ids["otherTenant"] = await SeedCandidateAsync(OtherTenantId, runId, CrmProspectingCandidateStatus.Duplicate, "other", TimeNewest);

            var collected = new List<long>();
            var totalCount = 0;
            for (var pageIndex = 1; pageIndex <= 3; pageIndex++)
            {
                var page = await _service.GetPageAsync(new CrmProspectingCandidatePageQueryDto
                {
                    RunId = runId,
                    Page = new PageRequestMetadata(pageIndex, PageSeedSize)
                });

                totalCount = page.Page.TotalCount;
                collected.AddRange(page.Items.Select(item => item.BasicId));
            }

            // Fixture sanity: the soft-deleted row must really be stored as deleted,
            // otherwise the exclusion below would prove nothing.
            var storedDeleted = await _db.Queryable<SysCrmProspectingCandidate>()
                .ClearFilter()
                .FirstAsync(candidate => candidate.BasicId == ids["deleted"]);
            Assert.True(storedDeleted.IsDeleted, "Fixture must persist IsDeleted=true at insert.");

            // Exclusions first, so a regressing filter names the exact leaked row.
            Assert.DoesNotContain(ids["deleted"], collected);
            Assert.DoesNotContain(ids["otherTenant"], collected);
            Assert.Equal(7, totalCount);

            // Exact stable order: CreatedTime DESC, then BasicId DESC inside each tie group.
            var expected = new[] { ids["c7"], ids["c6"], ids["c5"], ids["c4"], ids["c3"], ids["c2"], ids["c1"] };
            Assert.Equal(expected, collected);
            Assert.Equal(expected.Length, collected.Distinct().Count());
        }
    }

    [Fact]
    public async Task CandidatePage_ShouldCombineStatusFilterWithPaging()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync(TenantId, OwnerUserId);
            var pending = new List<long>();
            var duplicate = new List<long>();
            for (var i = 0; i < 5; i++)
            {
                pending.Add(await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.PendingReview, $"p{i}"));
            }

            for (var i = 0; i < 3; i++)
            {
                duplicate.Add(await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.Duplicate, $"d{i}"));
            }

            var seen = new List<long>();
            for (var pageIndex = 1; ; pageIndex++)
            {
                var page = await _service.GetPageAsync(new CrmProspectingCandidatePageQueryDto
                {
                    RunId = runId,
                    Status = (int)CrmProspectingCandidateStatus.Duplicate,
                    Page = new PageRequestMetadata(pageIndex, 2)
                });

                Assert.Equal(3, page.Page.TotalCount);
                var items = page.Items.ToArray();
                if (items.Length == 0)
                {
                    break;
                }

                Assert.All(items, item => Assert.Equal((int)CrmProspectingCandidateStatus.Duplicate, item.Status));
                seen.AddRange(items.Select(item => item.BasicId));
                if (items.Length < 2)
                {
                    break;
                }
            }

            Assert.Equal(duplicate.OrderBy(id => id), seen.OrderBy(id => id));
            Assert.DoesNotContain(pending, id => seen.Contains(id));

            // The status filter narrows the set: without it the Run has all 8 rows.
            Assert.Equal(8, await FetchTotalAsync(runId, null));

            // An undefined status fails fast instead of silently widening the filter.
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.GetPageAsync(
                new CrmProspectingCandidatePageQueryDto
                {
                    RunId = runId,
                    Status = 99,
                    Page = new PageRequestMetadata(1, 10)
                }));
        }
    }

    [Fact]
    public async Task CandidatePage_ShouldApplyTriStateShortlistFilter()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync(TenantId, OwnerUserId);
            var shortPending = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.PendingReview, "short-pending");
            var shortDuplicate = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.Duplicate, "short-dup");
            var plain = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.PendingReview, "plain");
            await _candidates.SetShortlistedAsync(shortPending, DateTimeOffset.UtcNow);
            await _candidates.SetShortlistedAsync(shortDuplicate, DateTimeOffset.UtcNow);

            // null keeps every row of the Run.
            Assert.Equal(3, (await FetchIdsAsync(runId, shortlisted: null)).Count);

            // true selects the marked rows regardless of their review status.
            var marked = await FetchIdsAsync(runId, shortlisted: true);
            Assert.Equal(2, marked.Count);
            Assert.Contains(shortPending, marked);
            Assert.Contains(shortDuplicate, marked);

            // false selects only the unmarked rows: this pins the IS NULL translation.
            var unmarked = await FetchIdsAsync(runId, shortlisted: false);
            Assert.Equal(new[] { plain }, unmarked);

            // The shortlist filter composes with the status filter instead of replacing it.
            var combined = await _service.GetPageAsync(new CrmProspectingCandidatePageQueryDto
            {
                RunId = runId,
                Status = (int)CrmProspectingCandidateStatus.Duplicate,
                Shortlisted = true,
                Page = new PageRequestMetadata(1, 10)
            });
            Assert.Single(combined.Items);
            Assert.Equal(shortDuplicate, combined.Items[0].BasicId);
        }
    }

    [Fact]
    public async Task CandidatePage_ShouldRejectInvalidRunAndOutOfScopeCaller()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync(TenantId, OwnerUserId);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.GetPageAsync(
                new CrmProspectingCandidatePageQueryDto { RunId = 0, Page = new PageRequestMetadata(1, 10) }));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetPageAsync(
                new CrmProspectingCandidatePageQueryDto { RunId = 999_999, Page = new PageRequestMetadata(1, 10) }));

            // Campaign is owned by OwnerUserId; a scope that excludes the owner must be denied.
            _scope = new UserDataScopeFilter(false, [OwnerUserId + 1]);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.GetPageAsync(
                new CrmProspectingCandidatePageQueryDto { RunId = runId, Page = new PageRequestMetadata(1, 10) }));

            // The same caller keeps access once the scope includes the owner.
            _scope = new UserDataScopeFilter(false, [OwnerUserId]);
            var page = await _service.GetPageAsync(new CrmProspectingCandidatePageQueryDto
            {
                RunId = runId,
                Page = new PageRequestMetadata(1, 10)
            });
            Assert.Empty(page.Items);
        }
    }

    [Fact]
    public async Task CandidateExportRows_ShouldHonorSnapshotRunAndStatusFilters()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync(TenantId, OwnerUserId);
            var otherRunId = await SeedRunAsync(TenantId, OwnerUserId);

            var dupA = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.Duplicate, "dup-a");
            var dupB = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.Duplicate, "dup-b");
            var dupC = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.Duplicate, "dup-c");
            await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.PendingReview, "pending-a");
            await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.PendingReview, "pending-b");
            await SeedCandidateAsync(TenantId, otherRunId, CrmProspectingCandidateStatus.Duplicate, "other-run");
            await SeedCandidateAsync(OtherTenantId, runId, CrmProspectingCandidateStatus.Duplicate, "other-tenant");

            var provider = new CrmProspectingCandidateExportProvider(_service);
            var columns = new List<ExportColumnDto>
            {
                new() { Key = "BasicId", Title = "ID" },
                new() { Key = "CompanyName", Title = "公司" },
                new() { Key = "Status", Title = "状态", ValueMap = new Dictionary<string, string> { ["1"] = "重复" } }
            };

            // runId + status snapshot: only this Run's duplicates come back, other Runs,
            // other tenants and other statuses stay out, and the column contract holds.
            var filteredRows = await DrainAsync(provider, Snapshot(runId, (int)CrmProspectingCandidateStatus.Duplicate), columns);
            Assert.Equal(3, filteredRows.Count);
            Assert.Equal(3, _lastContextTotal);
            Assert.Equal(
                new[] { dupA, dupB, dupC }.OrderBy(id => id).ToArray(),
                filteredRows.Select(row => long.Parse(row[0], CultureInfo.InvariantCulture)).OrderBy(id => id).ToArray());
            Assert.Equal(
                new[] { "dup-a", "dup-b", "dup-c" },
                filteredRows.Select(row => row[1]).OrderBy(name => name, StringComparer.Ordinal).ToArray());
            // Status column: raw code rendered as invariant string, then ValueMap → label.
            Assert.All(filteredRows, row => Assert.Equal("重复", row[2]));

            // Without the status filter the Run's full review queue exports (5 rows), still
            // excluding other Runs, other tenants and everything outside this tenant's scope.
            var allRows = await DrainAsync(provider, Snapshot(runId, null), columns);
            Assert.Equal(5, allRows.Count);
            Assert.Equal(5, _lastContextTotal);
            Assert.Equal(
                new[] { "dup-a", "dup-b", "dup-c", "pending-a", "pending-b" },
                allRows.Select(row => row[1]).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }
    }

    [Fact]
    public async Task CandidateExportRows_ShouldHonorShortlistedSnapshotFilter()
    {
        using (_tenant.Change(TenantId))
        {
            var runId = await SeedRunAsync(TenantId, OwnerUserId);
            var markedA = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.PendingReview, "marked-a");
            var markedB = await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.Duplicate, "marked-b");
            await SeedCandidateAsync(TenantId, runId, CrmProspectingCandidateStatus.PendingReview, "unmarked");
            await _candidates.SetShortlistedAsync(markedA, DateTimeOffset.UtcNow);
            await _candidates.SetShortlistedAsync(markedB, DateTimeOffset.UtcNow);

            var provider = new CrmProspectingCandidateExportProvider(_service);
            var columns = new List<ExportColumnDto>
            {
                new() { Key = "BasicId", Title = "ID" },
                new() { Key = "CompanyName", Title = "公司" }
            };

            var shortlistedRows = await DrainAsync(provider, Snapshot(runId, null, shortlisted: true), columns);
            Assert.Equal(2, shortlistedRows.Count);
            Assert.Equal(2, _lastContextTotal);
            Assert.Equal(
                new[] { "marked-a", "marked-b" },
                shortlistedRows.Select(row => row[1]).OrderBy(name => name, StringComparer.Ordinal).ToArray());

            var unshortlistedRows = await DrainAsync(provider, Snapshot(runId, null, shortlisted: false), columns);
            Assert.Single(unshortlistedRows);
            Assert.Equal("unmarked", unshortlistedRows[0][1]);
        }
    }

    private async Task<List<IReadOnlyList<string>>> DrainAsync(
        CrmProspectingCandidateExportProvider provider,
        string snapshot,
        IReadOnlyList<ExportColumnDto> columns)
    {
        var context = new ExportContext
        {
            FieldSecurity = new Mock<IFieldSecurityService>().Object,
            BusinessType = "crm.prospecting.candidate",
            Scope = ExportScope.SearchResult,
            QuerySnapshot = snapshot,
            Columns = columns
        };

        var rows = new List<IReadOnlyList<string>>();
        await foreach (var row in provider.ReadRowsAsync(context))
        {
            rows.Add(row);
        }

        _lastContextTotal = context.Total;
        return rows;
    }

    private static string Snapshot(long runId, int? status, bool? shortlisted = null)
        => JsonSerializer.Serialize(
            new CrmProspectingCandidatePageQueryDto
            {
                RunId = runId,
                Status = status,
                Shortlisted = shortlisted,
                Page = new PageRequestMetadata(1, 20)
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private async Task<List<long>> FetchIdsAsync(long runId, bool? shortlisted)
    {
        var page = await _service.GetPageAsync(new CrmProspectingCandidatePageQueryDto
        {
            RunId = runId,
            Shortlisted = shortlisted,
            Page = new PageRequestMetadata(1, 10)
        });
        return page.Items.Select(item => item.BasicId).ToList();
    }

    private async Task<int> FetchTotalAsync(long runId, int? status)
    {
        var page = await _service.GetPageAsync(new CrmProspectingCandidatePageQueryDto
        {
            RunId = runId,
            Status = status,
            Page = new PageRequestMetadata(1, 10)
        });
        return page.Page.TotalCount;
    }

    private async Task<long> SeedRunAsync(long tenantId, long? ownerUserId)
    {
        using (_tenant.Change(tenantId))
        {
            var campaign = await _campaigns.AddAsync(new SysCrmProspectingCampaign
            {
                Name = $"campaign-{Guid.NewGuid():N}",
                OwnerUserId = ownerUserId
            });
            var run = await _runs.AddAsync(new SysCrmProspectingRun
            {
                CampaignId = campaign.BasicId,
                StrategyId = 201,
                StrategyVersion = 1,
                RequestKey = Guid.NewGuid().ToString("N"),
                PayloadHash = new string('A', 64),
                SnapshotSearchObjective = "Candidate paging integration test",
                SnapshotProviderPlanJson = "[\"fake\"]",
                Status = CrmProspectingRunStatus.Succeeded
            });
            return run.BasicId;
        }
    }

    private async Task<long> SeedCandidateAsync(
        long tenantId,
        long runId,
        CrmProspectingCandidateStatus status,
        string externalId,
        DateTimeOffset? createdAt = null,
        bool deleted = false)
    {
        using (_tenant.Change(tenantId))
        {
            var candidate = new SysCrmProspectingCandidate
            {
                RunId = runId,
                ProviderCode = "provider-a",
                ExternalId = externalId,
                CompanyName = externalId,
                Status = status,
                IsDeleted = deleted
            };
            if (createdAt.HasValue)
            {
                // Seeded at insert: the framework treats CreatedTime as a create-only audit
                // column, so post-insert stamping cannot express the paging timeline.
                candidate.CreatedTime = createdAt.Value;
            }

            var added = await _candidates.AddAsync(candidate);
            return added.BasicId;
        }
    }

    public void Dispose()
    {
        _tenantScope.Dispose();
        AsyncLocalCurrentTenantAccessor.Instance.Current = null;
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static long ScopeId() => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;
}
