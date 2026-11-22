// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Services;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Research;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Security.Users;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Local background research: deterministic aggregation, auditable regeneration, review CAS.</summary>
public sealed class CrmBackgroundResearchTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=kiger-research-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly CrmBackgroundResearchAppService _service;
    private readonly CrmCustomerRepository _customers;
    private readonly CrmOpportunityRepository _opportunities;
    private readonly CrmLeadRepository _leads;

    public CrmBackgroundResearchTests()
    {
        _tenantScope = _tenant.Change(51);
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
        _db.CodeFirst.InitTables(
            typeof(SysCrmCustomer), typeof(SysCrmOpportunity), typeof(SysCrmLead),
            typeof(SysCrmBackgroundResearchReport), typeof(SysCrmResearchEvidence),
            typeof(SysCrmMarketOpportunity));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        var user = new Mock<ICurrentUser>();
        user.SetupGet(value => value.UserId).Returns(1001);
        _customers = new CrmCustomerRepository(_resolver.Object);
        _opportunities = new CrmOpportunityRepository(_resolver.Object);
        _leads = new CrmLeadRepository(_resolver.Object);
        _service = new CrmBackgroundResearchAppService(
            new CrmBackgroundResearchRepository(_resolver.Object),
            _customers, _opportunities, _leads, user.Object);
    }

    [Fact]
    public void Policy_ShouldBeDeterministicAndBounded()
    {
        Assert.Equal(100, CrmBackgroundResearchPolicy.OpportunityPriority(40_000m, 100));
        Assert.Equal(100, CrmBackgroundResearchPolicy.OpportunityPriority(40_000m, 100));
        Assert.Equal(60, CrmBackgroundResearchPolicy.OpportunityPriority(0m, 100));
        Assert.True(CrmBackgroundResearchPolicy.OpportunityPriority(1_000_000m, 100) <= 100);
        Assert.False(CrmBackgroundResearchPolicy.IsIncluded(
            new CrmResearchEvidenceDraft(CrmResearchSource.CustomerProfile, "Notes", "x", null, 39)));
        Assert.True(CrmBackgroundResearchPolicy.IsIncluded(
            new CrmResearchEvidenceDraft(CrmResearchSource.CustomerProfile, "Notes", "x", null, 50)));
        Assert.Equal(500, CrmBackgroundResearchPolicy.Truncate(new string('x', 600)).Length);
    }

    [Fact]
    public async Task Aggregate_ShouldUseOnlyRealLocalSources()
    {
        var customer = await _customers.AddAsync(new SysCrmCustomer
        {
            Name = "Acme",
            LegalName = "Acme Systems GmbH",
            Industry = "Manufacturing",
            CountryRegion = "DE",
            Website = "https://acme.example",
            Source = "Exhibition",
            Notes = new string('n', 600)
        });
        var prospect = await _opportunities.AddAsync(new SysCrmOpportunity
        {
            CustomerId = customer.BasicId,
            Name = "Line expansion",
            Stage = CrmOpportunityStage.Proposal,
            Amount = 25_000m,
            Currency = "EUR",
            Probability = 80
        });
        var closed = await _opportunities.AddAsync(new SysCrmOpportunity
        {
            CustomerId = customer.BasicId,
            Name = "Old deal",
            Stage = CrmOpportunityStage.Won,
            Amount = 90_000m,
            Currency = "EUR",
            Probability = 100
        });
        var lead = await _leads.AddAsync(new SysCrmLead
        {
            CompanyName = "Acme Trading",
            ContactName = "Ms. Meyer"
        });
        customer.ConvertedLeadId = lead.BasicId;
        await _customers.UpdateAsync(customer);

        var allOpportunities = await _opportunities.GetAllAsync();
        var aggregate = CrmBackgroundResearchAggregator.Aggregate(
            customer,
            allOpportunities.Where(item => item.CustomerId == customer.BasicId),
            [lead]);

        Assert.Contains(aggregate.Evidence, item => item.FactKey == CrmBackgroundResearchAggregator.FactIndustry);
        Assert.Contains(aggregate.Evidence, item => item.FactKey == CrmBackgroundResearchAggregator.FactOpenOpportunity
            && item.SourceRef == $"Crm_Opportunity:{prospect.BasicId}");
        Assert.Contains(aggregate.Evidence, item => item.Source == CrmResearchSource.SourceLead);
        Assert.All(aggregate.Evidence, item => Assert.True(item.FactValue.Length <= 500));
        Assert.Single(aggregate.Opportunities);
        Assert.Equal("Line expansion", aggregate.Opportunities[0].Title);
        Assert.DoesNotContain("Old deal", aggregate.Opportunities.Select(item => item.Title));
    }

    [Fact]
    public async Task Flow_ShouldGenerateRegenerateAndReview()
    {
        var customer = await _customers.AddAsync(new SysCrmCustomer
        {
            Name = "Flow Customer",
            Industry = "Logistics"
        });
        await _opportunities.AddAsync(new SysCrmOpportunity
        {
            CustomerId = customer.BasicId,
            Name = "Pilot",
            Stage = CrmOpportunityStage.NeedsAnalysis,
            Amount = 12_000m,
            Currency = "USD",
            Probability = 50
        });

        var first = await _service.GenerateAsync(customer.BasicId);
        Assert.Equal(CrmResearchReportStatus.Current, (CrmResearchReportStatus)first.Report.Status);
        Assert.NotEmpty(first.Evidence);
        Assert.Single(first.Opportunities);
        Assert.Equal((int)CrmMarketOpportunityStatus.Proposed, first.Opportunities[0].Status);

        var current = await _service.GetCurrentAsync(customer.BasicId);
        Assert.Equal(first.Report.BasicId, current!.Report.BasicId);

        var second = await _service.GenerateAsync(customer.BasicId);
        Assert.NotEqual(first.Report.BasicId, second.Report.BasicId);
        Assert.Equal(2, (await _service.GetHistoryAsync(customer.BasicId)).Count);
        Assert.Equal(second.Report.BasicId, (await _service.GetCurrentAsync(customer.BasicId))!.Report.BasicId);

        var review = await _service.ReviewOpportunityAsync(new CrmMarketOpportunityReviewDto
        {
            OpportunityId = second.Opportunities[0].BasicId,
            Status = (int)CrmMarketOpportunityStatus.Confirmed
        });
        Assert.Equal((int)CrmMarketOpportunityStatus.Confirmed, review.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ReviewOpportunityAsync(
            new CrmMarketOpportunityReviewDto
            {
                OpportunityId = second.Opportunities[0].BasicId,
                Status = (int)CrmMarketOpportunityStatus.Dismissed
            }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.ReviewOpportunityAsync(
            new CrmMarketOpportunityReviewDto
            {
                OpportunityId = second.Opportunities[0].BasicId,
                Status = 99
            }));
    }

    [Fact]
    public void Contract_ShouldGuardPermissionsAndMigration()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "AppServices", "CrmBackgroundResearchAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.62", "5.9.62.sql");
        Assert.Contains("Tag = \"BackgroundResearch\"", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Update)]", service, StringComparison.Ordinal);
        Assert.Contains("CrmBackgroundResearchAggregator.Aggregate", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Random", service, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_background_research_report", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_research_evidence", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_market_opportunity", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Exporter_ShouldRenderDeterministicFormats()
    {
        Assert.Equal(CrmBackgroundResearchExporter.Markdown,
            CrmBackgroundResearchExporter.NormalizeFormat(" MARKDOWN "));
        Assert.Equal(CrmBackgroundResearchExporter.Json,
            CrmBackgroundResearchExporter.NormalizeFormat("json"));
        Assert.Throws<ArgumentException>(() => CrmBackgroundResearchExporter.NormalizeFormat("xml"));

        var detail = new CrmBackgroundResearchDetailDto
        {
            Report = new CrmBackgroundResearchReportDto
            {
                CustomerName = "Acme", GeneratedAtUtc = DateTimeOffset.UtcNow,
                EvidenceCount = 1, OpportunityCount = 1
            },
            Evidence =
            [
                new CrmResearchEvidenceDto
                {
                    Source = (int)CrmResearchSource.OpenOpportunity,
                    FactKey = "OpenOpportunity", FactValue = "Proposal | 25000 USD", Confidence = 85
                }
            ],
            Opportunities =
            [
                new CrmMarketOpportunityDto
                {
                    Title = "Pilot", FactValue = "Proposal | 25000 USD", PriorityScore = 70
                }
            ]
        };
        var markdown = CrmBackgroundResearchExporter.RenderMarkdown(detail);
        Assert.Contains("\\|", markdown, StringComparison.Ordinal);
        Assert.Contains("## Evidence", markdown, StringComparison.Ordinal);
        Assert.Contains("## Market opportunities", markdown, StringComparison.Ordinal);
        Assert.Contains("\"evidence\"", CrmBackgroundResearchExporter.RenderJson(detail), StringComparison.Ordinal);
        Assert.EndsWith(".md", CrmBackgroundResearchExporter.BuildFileName(7, "markdown", DateTimeOffset.UtcNow), StringComparison.Ordinal);
        Assert.EndsWith(".json", CrmBackgroundResearchExporter.BuildFileName(7, "json", DateTimeOffset.UtcNow), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Flow_ShouldExportCurrentReport()
    {
        var customer = await _customers.AddAsync(new SysCrmCustomer
        {
            Name = "Export Customer", Industry = "Retail"
        });
        await _service.GenerateAsync(customer.BasicId);

        var markdown = await _service.GetExportAsync(customer.BasicId, "markdown");
        Assert.Equal("text/markdown", markdown.ContentType);
        Assert.Contains("Export Customer", markdown.Content, StringComparison.Ordinal);
        Assert.Contains("## Evidence", markdown.Content, StringComparison.Ordinal);

        var json = await _service.GetExportAsync(customer.BasicId, "json");
        Assert.Equal("application/json", json.ContentType);
        Assert.Contains("\"report\"", json.Content, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetExportAsync(customer.BasicId, "xml"));
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
