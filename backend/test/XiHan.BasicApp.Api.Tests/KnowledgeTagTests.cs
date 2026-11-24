// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.AI.Application.AppServices;
using XiHan.BasicApp.AI.Application.Dtos;
using XiHan.BasicApp.AI.Domain.Entities;
using XiHan.BasicApp.AI.Domain.Policies;
using XiHan.BasicApp.AI.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Knowledge tags and byte-identical duplicate detection.</summary>
public sealed class KnowledgeTagTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=kiger-ai-tags-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly KnowledgeTagAppService _service;
    private readonly KnowledgeDocumentRepository _documents;

    public KnowledgeTagTests()
    {
        _tenantScope = _tenant.Change(0);
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
        _db.CodeFirst.InitTables(typeof(SysKnowledgeDocument));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _documents = new KnowledgeDocumentRepository(_resolver.Object);
        _service = new KnowledgeTagAppService(_documents);
    }

    [Fact]
    public void Policy_ShouldNormalizeTagsHashAndGroupDuplicates()
    {
        var tags = KnowledgeTagPolicy.NormalizeTags(["  Product ", "PRODUCT", "Compliance", "", null!]);
        Assert.Equal(new[] { "compliance", "product" }, tags);
        Assert.Equal(tags, KnowledgeTagPolicy.ParseTags("[\"Product\",\"PRODUCT\",\"Compliance\"]"));
        Assert.Empty(KnowledgeTagPolicy.ParseTags(null));
        Assert.Throws<ArgumentException>(() => KnowledgeTagPolicy.ParseTags("{}"));
        var hash = KnowledgeTagPolicy.HashContent("same content");
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, KnowledgeTagPolicy.HashContent("same content"));
        Assert.NotEqual(hash, KnowledgeTagPolicy.HashContent("other"));

        var groups = KnowledgeTagPolicy.FindDuplicates(
        [
            (1, "a", hash),
            (2, "b", hash),
            (3, "c", null),
            (4, "d", "unique")
        ]);
        var group = Assert.Single(groups);
        Assert.Equal(new long[] { 1, 2 }, group.DocumentIds);
        Assert.Empty(KnowledgeTagPolicy.FindDuplicates([(1, "a", hash)]));
    }

    [Fact]
    public async Task Flow_ShouldSetTagsAndReportDuplicatesWithoutMerging()
    {
        var first = await _documents.AddAsync(new SysKnowledgeDocument
        {
            TenantId = 0,
            Title = "First",
            RawContent = "same content",
            ContentHash = KnowledgeTagPolicy.HashContent("same content")
        });
        var second = await _documents.AddAsync(new SysKnowledgeDocument
        {
            TenantId = 0,
            Title = "Second",
            RawContent = "same content",
            ContentHash = KnowledgeTagPolicy.HashContent("same content")
        });

        var report = await _service.GetDuplicatesAsync();
        Assert.Equal(1, report.Groups);
        Assert.Equal(2, report.DuplicateDocuments);
        Assert.Equal(2, report.Items[0].DocumentIds.Length);
        Assert.Equal(2, (await _db.Queryable<SysKnowledgeDocument>().ToListAsync()).Count);

        var tags = await _service.SetTagsAsync(new KnowledgeTagsUpdateDto
        {
            DocumentId = first.BasicId,
            Tags = [" Product ", "product", "Sales"]
        });
        Assert.Equal(2, tags.Length);
        Assert.Equal("product", tags[0].Tag);
        Assert.Equal(1, tags[0].DocumentCount);
        Assert.Equal("sales", tags[1].Tag);

        Assert.NotNull(await _documents.GetByContentHashAsync(KnowledgeTagPolicy.HashContent("same content")));
        Assert.Null(await _documents.GetByContentHashAsync("missing"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetTagsAsync(
            new KnowledgeTagsUpdateDto { DocumentId = 99999, Tags = ["x"] }));
    }

    [Fact]
    public void Contract_ShouldGuardPermissionsAndMigration()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.AI", "Application",
            "AppServices", "KnowledgeTagAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.57", "5.9.57.sql");
        Assert.Contains("Tag = \"KnowledgeTags\"", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(KnowledgePermissionCodes.Read)]", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", service, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE sys_knowledge_document ADD COLUMN IF NOT EXISTS content_hash", migration, StringComparison.Ordinal);
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
