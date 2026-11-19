// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.AI.Application.AppServices;
using XiHan.BasicApp.AI.Application.Dtos;
using XiHan.BasicApp.AI.Domain.Entities;
using XiHan.BasicApp.AI.Domain.Enums;
using XiHan.BasicApp.AI.Domain.Policies;
using XiHan.BasicApp.AI.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Security.Users;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Knowledge review: pure gating plus auditable decision lifecycle.</summary>
public sealed class KnowledgeReviewTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=kiger-ai-review-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly KnowledgeReviewAppService _service;

    public KnowledgeReviewTests()
    {
        _tenantScope = _tenant.Change(0); // platform data
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
        _db.CodeFirst.InitTables(typeof(SysKnowledgeDocument), typeof(SysKnowledgeDocumentReview));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        var user = new Mock<ICurrentUser>();
        user.SetupGet(value => value.UserId).Returns(42);
        _service = new KnowledgeReviewAppService(
            new KnowledgeDocumentRepository(_resolver.Object),
            new KnowledgeReviewRepository(_resolver.Object),
            user.Object);
    }

    [Fact]
    public void Policy_ShouldRequireReviewOnlyForNewOrChangedContent()
    {
        var hash = KnowledgeReviewPolicy.HashContent("hello");
        Assert.Equal(64, hash.Length);
        Assert.True(KnowledgeReviewPolicy.CanSubmit(null, hash, null).Allowed);
        Assert.True(KnowledgeReviewPolicy.CanSubmit(KnowledgeReviewStatus.Approved, "new-hash", hash).Allowed);
        Assert.False(KnowledgeReviewPolicy.CanSubmit(KnowledgeReviewStatus.Approved, hash, hash).Allowed);
        Assert.False(KnowledgeReviewPolicy.CanDecide(KnowledgeReviewStatus.Approved, true, null).Allowed);
        Assert.False(KnowledgeReviewPolicy.CanDecide(KnowledgeReviewStatus.Pending, false, null).Allowed);
        Assert.True(KnowledgeReviewPolicy.CanDecide(KnowledgeReviewStatus.Pending, false, "outdated").Allowed);
    }

    [Fact]
    public async Task ReviewFlow_ShouldAuditDecisionAndGateContentChanges()
    {
        var documentId = await SeedDocumentAsync("original");
        var pending = await _service.SubmitAsync(documentId);
        Assert.Equal((int)KnowledgeReviewStatus.Pending, pending.Status);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DecideAsync(new KnowledgeReviewDecisionDto { ReviewId = pending.BasicId, Approve = false }));
        var approved = await _service.DecideAsync(new KnowledgeReviewDecisionDto
        {
            ReviewId = pending.BasicId,
            Approve = true,
            Comment = "verified"
        });
        Assert.Equal((int)KnowledgeReviewStatus.Approved, approved.Status);
        Assert.Equal(42, approved.ReviewerId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SubmitAsync(documentId));
        await _db.Updateable<SysKnowledgeDocument>()
            .SetColumns(item => item.RawContent == "updated")
            .Where(item => item.BasicId == documentId)
            .ExecuteCommandAsync();
        var changed = await _service.SubmitAsync(documentId);
        Assert.Equal((int)KnowledgeReviewStatus.Pending, changed.Status);
        Assert.Equal(KnowledgeReviewPolicy.HashContent("updated"), changed.VersionHash);
        Assert.Equal(2, (await _service.GetReviewsAsync(documentId)).Count);
    }

    [Fact]
    public void Contract_ShouldCarryPermissionsAndStayDecoupledFromIndexing()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.AI",
            "Application", "AppServices", "KnowledgeReviewAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.51", "5.9.51.sql");
        Assert.Contains("[PermissionAuthorize(AiPermissionCodes.Execute)]", app, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(AiPermissionCodes.Read)]", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ReindexAsync", app, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS sys_knowledge_document_review", migration, StringComparison.Ordinal);
    }

    private async Task<long> SeedDocumentAsync(string content)
    {
        var added = await new KnowledgeDocumentRepository(_resolver.Object).AddAsync(new SysKnowledgeDocument
        {
            TenantId = 0,
            Title = "review-doc",
            RawContent = content,
            Source = "unit-test"
        });
        return added.BasicId;
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
