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
using XiHan.BasicApp.Crm.Infrastructure.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>M2: staged qualification is explainable, review-only and never overwrites candidate data.</summary>
public sealed class CrmProspectingQualificationTests : IDisposable
{
    private const long TenantId = 9961;
    private const long OwnerUserId = 321;

    private readonly string _connectionString = $"Data Source=kiger-crm-qual-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly Mock<IUserDataScopeFilterService> _dataScope = new();
    private readonly Mock<ICrmProspectingIdentityResolver> _identityResolver = new();
    private readonly Mock<ICrmProspectingPageFetcher> _pageFetcher = new();
    private readonly CrmProspectingCandidateRepository _candidates;
    private readonly CrmProspectingEvidenceRepository _evidence;
    private readonly CrmProspectingVerificationReportRepository _reports;
    private readonly CrmProspectingQualificationAppService _service;
    private UserDataScopeFilter _scope = UserDataScopeFilter.Unlimited;

    public CrmProspectingQualificationTests()
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
            typeof(SysCrmProspectingCampaign),
            typeof(SysCrmProspectingRun),
            typeof(SysCrmProspectingCandidate),
            typeof(SysCrmProspectingEvidence),
            typeof(SysCrmProspectingVerificationReport));

        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _candidates = new CrmProspectingCandidateRepository(_resolver.Object);
        _evidence = new CrmProspectingEvidenceRepository(_resolver.Object);
        _reports = new CrmProspectingVerificationReportRepository(_resolver.Object);
        var campaigns = new CrmProspectingCampaignRepository(_resolver.Object);
        var runs = new CrmProspectingRunRepository(_resolver.Object);
        _dataScope
            .Setup(value => value.ResolveAccessibleUsersAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(_scope));
        _service = new CrmProspectingQualificationAppService(
            _candidates, _evidence, _reports, runs, campaigns,
            _pageFetcher.Object, _dataScope.Object, _identityResolver.Object);
    }

    [Fact]
    public void Policy_ShouldReportGapsWithoutAuthoritativeIdentity()
    {
        var candidate = Candidate("ACME GmbH");
        var report = CrmProspectingVerificationPolicy.Build(candidate, [], null);

        Assert.Equal(CrmProspectingQualificationVerdict.NeedsReview, report.Verdict);
        Assert.Equal(CrmProspectingVerificationState.Unverified,
            report.Fields.Single(field => field.Field == "legalName").State);
        Assert.Contains("legalName", report.MissingFields);
        Assert.Contains("website", report.MissingFields);
        Assert.NotEmpty(report.RecommendedActions);
        Assert.Equal("none", report.IdentityAuthority);
        Assert.Empty(report.Blockers);
    }

    [Fact]
    public void Policy_ShouldPromoteOnlyExactIdentityAndCorroboratedEvidence()
    {
        var candidate = Candidate("ACME GmbH");
        candidate.Website = "https://acme.example.com";
        candidate.BusinessEmail = "sales@acme.example.com";
        candidate.ContactName = "Jane Doe";
        var match = new CrmProspectingLegalEntityMatch(
            "529900ACME123", "ACME GmbH", "ACTIVE", "ACTIVE", "DE", null,
            "https://api.gleif.org/api/v1/lei-records/529900ACME123", "hash", DateTimeOffset.UtcNow);
        var evidence = new[]
        {
            Evidence("business_email", "email"),
            Evidence("contact_name", "contact"),
            Evidence("country", "country"),
        };

        var report = CrmProspectingVerificationPolicy.Build(candidate, evidence, match);

        // A review-accepted website stays Unverified by design: the policy never declares it official.
        Assert.Equal(CrmProspectingQualificationVerdict.NeedsReview, report.Verdict);
        Assert.Equal("GLEIF:529900ACME123", report.IdentityAuthority);
        Assert.All(report.Fields.Where(field => field.Field != "website"),
            field => Assert.Equal(CrmProspectingVerificationState.Verified, field.State));
        Assert.Equal(CrmProspectingVerificationState.Unverified,
            report.Fields.Single(field => field.Field == "website").State);
        Assert.Empty(report.MissingFields);
        Assert.DoesNotContain("website", report.MissingFields);
    }

    [Fact]
    public void Policy_ShouldBlockOnUnsafeWebsiteAndMismatchedIdentity()
    {
        var candidate = Candidate("ACME GmbH");
        candidate.Website = "http://facebook.com/company";
        var wrongMatch = new CrmProspectingLegalEntityMatch(
            "529900OTHER", "Other Corp", "ACTIVE", "ACTIVE", "FR", null, "https://example.com", "hash", DateTimeOffset.UtcNow);

        var report = CrmProspectingVerificationPolicy.Build(candidate, [], wrongMatch);

        Assert.Equal(CrmProspectingQualificationVerdict.Blocked, report.Verdict);
        Assert.Equal(2, report.Blockers.Count);
        Assert.Contains(report.Fields, field => field.Field == "legalName" && field.State == CrmProspectingVerificationState.Conflict);
        Assert.Contains(report.Fields, field => field.Field == "website" && field.State == CrmProspectingVerificationState.Conflict);
    }

    [Fact]
    public async Task BuildAndGet_ShouldFreezeReportAndKeepCandidateUntouched()
    {
        var candidateId = await SeedCandidateAsync();
        var before = await _candidates.GetByIdAsync(candidateId);

        var report = await _service.BuildVerificationReportAsync(new CrmProspectingVerificationBuildDto { CandidateId = candidateId });
        var latest = await _service.GetVerificationReportAsync(candidateId);

        Assert.Equal(report.BasicId, latest!.BasicId);
        Assert.Equal(candidateId, report.CandidateId);
        Assert.Equal(report.Summary, latest.Summary);
        Assert.Equal(CrmProspectingQualificationVerdict.NeedsReview, (CrmProspectingQualificationVerdict)report.Verdict);
        var after = await _candidates.GetByIdAsync(candidateId);
        Assert.Equal(before!.CompanyName, after!.CompanyName);
        Assert.Equal(before.Website, after.Website);
        Assert.Equal(before.Status, after.Status);
    }

    [Fact]
    public async Task QualificationCommands_ShouldRejectForeignCandidateAndCarryContract()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.BuildVerificationReportAsync(new CrmProspectingVerificationBuildDto { CandidateId = 999 }));
        _scope = new UserDataScopeFilter(false, [OwnerUserId + 1]);
        var candidateId = await SeedCandidateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.BuildVerificationReportAsync(new CrmProspectingVerificationBuildDto { CandidateId = candidateId }));

        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "AppServices", "CrmProspectingQualificationAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.45", "5.9.45.sql");
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]\n    public async Task<CrmProspectingIdentityMatchDto?> ResolveIdentityAsync", app, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]\n    public Task<CrmProspectingWebsiteAssessmentDto> AssessWebsiteAsync", app, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]\n    public async Task<CrmProspectingVerificationReportDto> BuildVerificationReportAsync", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Status = CrmProspectingCandidateStatus", app, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_prospecting_verification_report", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void BootstrapPolicy_ShouldPromoteOnlyUniqueCountryCompatibleExactMatch()
    {
        var acme = new CrmProspectingLegalEntityMatch(
            "LEI-1", "ACME GmbH", "ACTIVE", "ACTIVE", "DE", null, "https://example.com/1", "h1", DateTimeOffset.UtcNow);
        var unique = CrmProspectingIdentityBootstrapPolicy.Build("ACME GmbH", "DE", [acme]);
        Assert.Equal(CrmProspectingIdentityBootstrapStage.ExactRegistryMatch, unique.Stage);
        Assert.Equal("LEI-1", unique.AuthoritativeMatch!.Lei);
        Assert.NotEmpty(unique.Steps);

        var ambiguous = CrmProspectingIdentityBootstrapPolicy.Build("ACME GmbH", null, [acme, acme with { Lei = "LEI-2" }]);
        Assert.Equal(CrmProspectingIdentityBootstrapStage.NeedsOperatorReconciliation, ambiguous.Stage);
        Assert.Null(ambiguous.AuthoritativeMatch);
        Assert.Equal(2, ambiguous.Candidates.Count);

        Assert.Equal(CrmProspectingIdentityBootstrapStage.Unresolved,
            CrmProspectingIdentityBootstrapPolicy.Build("ACME GmbH", "DE", []).Stage);
    }

    [Fact]
    public async Task Bootstrap_ShouldUseResolverAndExposeExplainableStages()
    {
        var acme = new CrmProspectingLegalEntityMatch(
            "LEI-1", "ACME GmbH", "ACTIVE", "ACTIVE", "DE", null, "https://example.com/1", "h1", DateTimeOffset.UtcNow);
        _identityResolver
            .Setup(value => value.DiscoverAsync("ACME GmbH", It.IsAny<CancellationToken>()))
            .ReturnsAsync([acme]);

        var result = await _service.BootstrapIdentityAsync("ACME GmbH", "DE");

        Assert.Equal((int)CrmProspectingIdentityBootstrapStage.ExactRegistryMatch, result.Stage);
        Assert.Equal("LEI-1", result.AuthoritativeMatch!.Lei);
        Assert.Contains(result.Steps, step => step.ReasonCode == "exact_registry_match");
    }

    [Fact]
    public async Task ProbeWebsite_ShouldPersistBoundedEvidenceOnlyOnSuccessAndBeIdempotent()
    {
        var candidateId = await SeedCandidateAsync();
        var fetch = new CrmProspectingPageFetchResult("https://acme.example.com", 200, "ACME", "bodyhash", 512, "ok");
        _pageFetcher
            .Setup(value => value.FetchAsync("https://acme.example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fetch);
        var input = new CrmProspectingWebsiteProbeInputDto
        {
            CandidateId = candidateId,
            CompanyName = "ACME GmbH",
            CandidateUrl = "https://acme.example.com"
        };

        var first = await _service.ProbeWebsiteAsync(input);
        var replay = await _service.ProbeWebsiteAsync(input);
        Assert.True(first.Succeeded);
        Assert.True(first.EvidencePersisted);
        Assert.True(replay.EvidencePersisted);
        Assert.Single(await _evidence.GetListAsync(item => item.CandidateId == candidateId));

        _pageFetcher
            .Setup(value => value.FetchAsync("https://acme.example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CrmProspectingPageFetchResult("https://acme.example.com", 0, null, string.Empty, 0, "fetch_failed"));
        var failed = await _service.ProbeWebsiteAsync(input);
        Assert.False(failed.Succeeded);
        Assert.False(failed.EvidencePersisted);
        Assert.Single(await _evidence.GetListAsync(item => item.CandidateId == candidateId));
    }

    [Fact]
    public async Task PinnedFetcher_ShouldRejectUnsafeUrlBeforeAnyNetworkCall()
    {
        var fetcher = new CrmProspectingPinnedPageFetcher();
        var result = await fetcher.FetchAsync("http://facebook.com/company");
        Assert.False(result.Succeeded);
        Assert.Equal("unsafe_url_shape", result.ReasonCode);
    }

    [Fact]
    public async Task Qualification_ShouldStartWithoutOptInResolverAndReportItHonestly()
    {
        // The GLEIF resolver is opt-in; preview servers without the switch must still boot.
        var service = new CrmProspectingQualificationAppService(
            _candidates, _evidence, _reports,
            new CrmProspectingRunRepository(_resolver.Object),
            new CrmProspectingCampaignRepository(_resolver.Object),
            _pageFetcher.Object,
            _dataScope.Object);
        var bootstrap = await service.BootstrapIdentityAsync("ACME GmbH");
        Assert.Equal((int)CrmProspectingIdentityBootstrapStage.Unresolved, bootstrap.Stage);
        Assert.Contains(bootstrap.Steps, step => step.ReasonCode == "resolver_not_registered");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveIdentityAsync("ACME GmbH"));
    }

    private static SysCrmProspectingCandidate Candidate(string companyName) => new()
    {
        CompanyName = companyName,
        ProviderCode = "provider-a",
        ExternalId = "ext-1",
        Status = CrmProspectingCandidateStatus.PendingReview
    };

    private static SysCrmProspectingEvidence Evidence(string fieldName, string token) => new()
    {
        CandidateId = 1,
        ProviderCode = "provider-a",
        FieldName = fieldName + "-" + token,
        SourceUrl = "https://acme.example.com",
        EvidenceType = "direct",
        VerificationStatus = "verified",
        RetrievedAtUtc = DateTimeOffset.UtcNow
    };

    private async Task<long> SeedCandidateAsync()
    {
        var campaign = await new CrmProspectingCampaignRepository(_resolver.Object).AddAsync(new SysCrmProspectingCampaign
        {
            Name = "qual-campaign",
            OwnerUserId = OwnerUserId,
            Status = CrmProspectingCampaignStatus.Active
        });
        var run = await new CrmProspectingRunRepository(_resolver.Object).AddAsync(new SysCrmProspectingRun
        {
            CampaignId = campaign.BasicId,
            StrategyId = 1,
            StrategyVersion = 1,
            RequestKey = "qual-run",
            PayloadHash = new string('A', 64),
            SnapshotSearchObjective = "qualification",
            SnapshotProviderPlanJson = "[]"
        });
        var candidate = await _candidates.AddAsync(new SysCrmProspectingCandidate
        {
            RunId = run.BasicId,
            ProviderCode = "provider-a",
            ExternalId = "ext-1",
            CompanyName = "ACME GmbH",
            Status = CrmProspectingCandidateStatus.PendingReview
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
