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

/// <summary>Outreach drafts: whitelist interpolation, explicit review and no silent fallback.</summary>
public sealed class CrmOutreachDraftTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=kiger-draft-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly CrmOutreachDraftAppService _service;

    public CrmOutreachDraftTests()
    {
        _tenantScope = _tenant.Change(61);
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
            typeof(SysCrmProspectingCandidate), typeof(SysCrmOutreachSequence),
            typeof(SysCrmOutreachEnrollment), typeof(SysCrmOutreachDraft));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        var user = new Mock<ICurrentUser>();
        user.SetupGet(value => value.UserId).Returns(1001);
        _service = new CrmOutreachDraftAppService(
            new CrmOutreachDraftRepository(_resolver.Object),
            new CrmOutreachEnrollmentRepository(_resolver.Object),
            new CrmOutreachSequenceRepository(_resolver.Object),
            new CrmProspectingCandidateRepository(_resolver.Object),
            user.Object);
    }

    [Fact]
    public void Renderer_ShouldInterpolateWhitelistOnly()
    {
        var candidate = new SysCrmProspectingCandidate
        {
            CompanyName = "Acme GmbH",
            ContactName = "Anna Meyer",
            BusinessEmail = "anna@acme.example",
            Website = "https://acme.example"
        };
        var variables = CrmOutreachTemplateRenderer.BuildVariables(candidate);
        Assert.Equal("Anna", variables[CrmOutreachTemplateRenderer.FirstName]);
        var rendered = CrmOutreachTemplateRenderer.Render(
            "Hi {{FirstName}} at {{CompanyName}} ({{BusinessEmail}})", variables);
        Assert.Equal("Hi Anna at Acme GmbH (anna@acme.example)", rendered);
        Assert.Equal(CrmOutreachTemplateRenderer.ComputeHash(rendered),
            CrmOutreachTemplateRenderer.ComputeHash(rendered));

        Assert.Throws<InvalidOperationException>(() => CrmOutreachTemplateRenderer.Render(
            "Hi {{Secret}}", variables));
        Assert.Throws<InvalidOperationException>(() => CrmOutreachTemplateRenderer.Render(
            "Hi {{Summary}}", variables));
        var injectedVariables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CrmOutreachTemplateRenderer.CompanyName] = "{{FirstName}}",
            [CrmOutreachTemplateRenderer.FirstName] = "Anna"
        };
        Assert.Equal("{{FirstName}}", CrmOutreachTemplateRenderer.Render(
            "{{CompanyName}}", injectedVariables));
        Assert.Throws<ArgumentException>(() => CrmOutreachTemplateRenderer.Render(
            new string('x', CrmOutreachTemplateRenderer.MaxTemplateLength + 1), variables));
    }

    [Fact]
    public void Policy_ShouldGuardReviewTransitions()
    {
        Assert.True(CrmOutreachDraftPolicy.CanApprove(CrmOutreachDraftStatus.Planned));
        Assert.True(CrmOutreachDraftPolicy.CanReject(CrmOutreachDraftStatus.Planned));
        Assert.False(CrmOutreachDraftPolicy.CanApprove(CrmOutreachDraftStatus.Approved));
        Assert.False(CrmOutreachDraftPolicy.CanReject(CrmOutreachDraftStatus.Rejected));
        Assert.Equal("wrong tone", CrmOutreachDraftPolicy.NormalizeReason("  wrong tone  "));
        Assert.Throws<ArgumentException>(() => CrmOutreachDraftPolicy.NormalizeReason("  "));
    }

    [Fact]
    public async Task Flow_ShouldRenderCreateAndReviewDraft()
    {
        var candidateRepo = new CrmProspectingCandidateRepository(_resolver.Object);
        var candidate = await candidateRepo.AddAsync(new SysCrmProspectingCandidate
        {
            CompanyName = "Flow GmbH",
            ContactName = "Lena Schmidt",
            BusinessEmail = "lena@flow.example"
        });
        var sequenceRepo = new CrmOutreachSequenceRepository(_resolver.Object);
        var sequence = await sequenceRepo.AddAsync(new SysCrmOutreachSequence
        {
            Name = "flow-sequence",
            StepsJson = "[{\"channel\":0,\"delayDays\":0,\"template\":\"Hi {{FirstName}} at {{CompanyName}}\"}]"
        });
        var enrollmentRepo = new CrmOutreachEnrollmentRepository(_resolver.Object);
        var enrollment = await enrollmentRepo.AddAsync(new SysCrmOutreachEnrollment
        {
            SequenceId = sequence.BasicId,
            CandidateId = candidate.BasicId,
            Status = CrmOutreachEnrollmentStatus.Active,
            CurrentStep = 0
        });

        var preview = await _service.PreviewAsync(new CrmOutreachDraftPreviewDto
        {
            CandidateId = candidate.BasicId,
            Template = "Hi {{FirstName}} at {{CompanyName}}"
        });
        Assert.Equal("Hi Lena at Flow GmbH", preview);

        var draft = await _service.CreateAsync(new CrmOutreachDraftCreateDto
        {
            EnrollmentId = enrollment.BasicId,
            Step = 0
        });
        Assert.Equal((int)CrmOutreachDraftStatus.Planned, draft.Status);
        Assert.Equal("Hi Lena at Flow GmbH", draft.RenderedText);
        Assert.Equal(CrmOutreachTemplateRenderer.ComputeHash(draft.RenderedText), draft.ContentHash);
        Assert.Single(await _service.ListAsync(enrollment.BasicId));

        var approved = await _service.ApproveAsync(draft.BasicId);
        Assert.Equal((int)CrmOutreachDraftStatus.Approved, approved.Status);
        Assert.Equal(1001, approved.ApprovedBy);
        Assert.NotNull(approved.ApprovedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApproveAsync(draft.BasicId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RejectAsync(
            new CrmOutreachDraftReviewDto { DraftId = draft.BasicId, Reason = "late" }));

        var second = await _service.CreateAsync(new CrmOutreachDraftCreateDto
        {
            EnrollmentId = enrollment.BasicId,
            Step = 0
        });
        var rejected = await _service.RejectAsync(new CrmOutreachDraftReviewDto
        {
            DraftId = second.BasicId,
            Reason = "tone"
        });
        Assert.Equal((int)CrmOutreachDraftStatus.Rejected, rejected.Status);
        Assert.Equal("tone", rejected.RejectionReason);
        Assert.Equal(2, (await _service.ListAsync(enrollment.BasicId)).Count);
    }

    [Fact]
    public void Contract_ShouldGuardPermissionsAndMigration()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "AppServices", "CrmOutreachDraftAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.63", "5.9.63.sql");
        Assert.Contains("Tag = \"OutreachDraft\"", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", service, StringComparison.Ordinal);
        Assert.Contains("CrmOutreachTemplateRenderer.Render", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SmtpClient", service, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_outreach_draft", migration, StringComparison.Ordinal);
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
